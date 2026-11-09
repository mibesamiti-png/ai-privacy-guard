using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class LiveSafetyGateTests
{
    private static PixelBuffer Frame()
    {
        var bytes = new byte[8 * 8 * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = 120;
            bytes[i + 1] = 120;
            bytes[i + 2] = 120;
            bytes[i + 3] = 255;
        }
        return new PixelBuffer(8, 8, bytes, copy: false);
    }

    private static void AssertBlack(PixelBuffer frame)
    {
        for (int i = 0; i < frame.Bgra.Length; i += 4)
        {
            Assert.Equal((byte)0, frame.Bgra[i]);
            Assert.Equal((byte)0, frame.Bgra[i + 1]);
            Assert.Equal((byte)0, frame.Bgra[i + 2]);
            Assert.Equal((byte)255, frame.Bgra[i + 3]);
        }
    }

    private sealed class StubDetector(
        Func<PixelBuffer, CancellationToken, Task<IReadOnlyList<DetectionCandidate>>> run)
        : IExposureDetector
    {
        public string ModelId => "synthetic-test-detector";
        public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken ct)
            => run(image, ct);
        public void Dispose() { }
    }

    private static StubDetector Stub(params DetectionCandidate[] candidates) =>
        new((_, _) => Task.FromResult<IReadOnlyList<DetectionCandidate>>(candidates));

    [Fact]
    public async Task NoModelStaysBlackAndCannotArm()
    {
        var gate = new LiveSafetyGate(null);
        Assert.Throws<InvalidOperationException>(() => gate.EnableExperimentalProcessing());
        var input = Frame();
        var result = await gate.ProcessAsync(0, input);
        Assert.Equal(LiveFrameDisposition.Blocked, result.Disposition);
        Assert.False(result.DetectionCompleted);
        AssertBlack(result.Pixels);
        Assert.Equal((byte)120, input.Bgra[0]);
    }

    [Fact]
    public async Task ExplicitArmMasksDetectedRegionsWithoutChangingSource()
    {
        var d = Stub(new DetectionCandidate("TEST", 0.9,
            new NormalizedRect(0.25, 0.25, 0.5, 0.5)));
        var gate = new LiveSafetyGate(d, new LiveGateSettings(TimeSpan.FromSeconds(1), 0));
        gate.EnableExperimentalProcessing();
        var input = Frame();
        var result = await gate.ProcessAsync(0, input);
        Assert.Equal(LiveFrameDisposition.Masked, result.Disposition);
        Assert.True(result.DetectionCompleted);
        Assert.Equal((byte)0, result.Pixels.Bgra[(3 * 8 + 3) * 4]);
        Assert.Equal((byte)120, result.Pixels.Bgra[0]);
        Assert.Equal((byte)120, input.Bgra[(3 * 8 + 3) * 4]);
    }

    [Fact]
    public async Task EmptyDetectionIsScreenedNotCertifiedSafe()
    {
        var gate = new LiveSafetyGate(Stub());
        gate.EnableExperimentalProcessing();
        var original = Frame();
        var result = await gate.ProcessAsync(0, original);
        Assert.Equal(LiveFrameDisposition.Screened, result.Disposition);
        Assert.True(result.DetectionCompleted);
        Assert.Contains("NOT a safety guarantee", result.Reason);
        Assert.Equal(original.Bgra, result.Pixels.Bgra);
        Assert.NotSame(original.Bgra, result.Pixels.Bgra);
    }

    [Fact]
    public async Task DetectorErrorFailsClosedAndRequiresNewGate()
    {
        var gate = new LiveSafetyGate(
            new StubDetector((_, _) => throw new InvalidOperationException("synthetic failure")));
        gate.EnableExperimentalProcessing();
        var result = await gate.ProcessAsync(0, Frame());
        Assert.Equal(LiveFrameDisposition.DetectorFault, result.Disposition);
        AssertBlack(result.Pixels);
        Assert.True(gate.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => gate.EnableExperimentalProcessing());
        Assert.Equal(LiveFrameDisposition.Blocked, (await gate.ProcessAsync(1, Frame())).Disposition);
    }

    [Fact]
    public async Task TimedOutDetectorDisarmsAndNeverEmitsUnprocessedFrame()
    {
        var incomplete = new TaskCompletionSource<IReadOnlyList<DetectionCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new LiveSafetyGate(new StubDetector((_, _) => incomplete.Task),
            new LiveGateSettings(TimeSpan.FromMilliseconds(20)));
        gate.EnableExperimentalProcessing();
        var result = await gate.ProcessAsync(0, Frame());
        Assert.Equal(LiveFrameDisposition.TimedOut, result.Disposition);
        AssertBlack(result.Pixels);
        Assert.True(gate.IsFaulted);
        incomplete.TrySetResult(Array.Empty<DetectionCandidate>());
        var next = await gate.ProcessAsync(1, Frame());
        Assert.Equal(LiveFrameDisposition.Blocked, next.Disposition);
        AssertBlack(next.Pixels);
    }

    [Fact]
    public async Task InvalidDetectionCoordinatesFailClosed()
    {
        var gate = new LiveSafetyGate(Stub(
            new DetectionCandidate("BAD", double.NaN,
                new NormalizedRect(0.3, 0.3, 0.2, 0.2))));
        gate.EnableExperimentalProcessing();
        var result = await gate.ProcessAsync(0, Frame());
        Assert.Equal(LiveFrameDisposition.InvalidDetection, result.Disposition);
        AssertBlack(result.Pixels);
        Assert.True(gate.IsFaulted);
    }

    [Fact]
    public async Task OutOfOrderFramesAreBlackedOut()
    {
        var gate = new LiveSafetyGate(Stub());
        gate.EnableExperimentalProcessing();
        Assert.Equal(LiveFrameDisposition.Screened,
            (await gate.ProcessAsync(5, Frame())).Disposition);
        var stale = await gate.ProcessAsync(4, Frame());
        Assert.Equal(LiveFrameDisposition.OutOfOrder, stale.Disposition);
        AssertBlack(stale.Pixels);
    }

    [Fact]
    public async Task DisableWhileDetectorRunsPreventsFrameRelease()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<DetectionCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new LiveSafetyGate(new StubDetector((_, _) =>
        {
            started.TrySetResult(true);
            return release.Task;
        }), new LiveGateSettings(TimeSpan.FromSeconds(2)));
        gate.EnableExperimentalProcessing();
        var processing = gate.ProcessAsync(0, Frame());
        await started.Task;
        gate.Disable();
        release.TrySetResult(Array.Empty<DetectionCandidate>());
        var result = await processing;
        Assert.Equal(LiveFrameDisposition.Blocked, result.Disposition);
        AssertBlack(result.Pixels);
    }

    [Fact]
    public async Task ConcurrentFrameNeverWaitsBehindRunningDetector()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<DetectionCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new LiveSafetyGate(new StubDetector((_, _) =>
        {
            started.TrySetResult(true);
            return release.Task;
        }), new LiveGateSettings(TimeSpan.FromSeconds(2)));
        gate.EnableExperimentalProcessing();
        var first = gate.ProcessAsync(0, Frame());
        await started.Task;
        var second = await gate.ProcessAsync(1, Frame());
        Assert.Equal(LiveFrameDisposition.Busy, second.Disposition);
        AssertBlack(second.Pixels);
        release.TrySetResult(Array.Empty<DetectionCandidate>());
        Assert.Equal(LiveFrameDisposition.Screened, (await first).Disposition);
    }

    private sealed class CollectingSink : ILiveFrameSink
    {
        public readonly List<LiveFrameResult> Received = new();
        public ValueTask WriteAsync(LiveFrameResult result, CancellationToken ct)
        {
            Received.Add(result);
            return ValueTask.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<LiveInputFrame> SyntheticFrames()
    {
        yield return new LiveInputFrame(0, Frame());
        yield return new LiveInputFrame(1, Frame());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PipelineNeverSendsRawFramesWhenUnarmed()
    {
        var sink = new CollectingSink();
        await LiveFramePipeline.RunAsync(SyntheticFrames(), new LiveSafetyGate(null),
            sink, CancellationToken.None);
        Assert.Equal(2, sink.Received.Count);
        foreach (var frame in sink.Received)
        {
            Assert.Equal(LiveFrameDisposition.Blocked, frame.Disposition);
            AssertBlack(frame.Pixels);
        }
    }

    [Fact]
    public void InvalidTimeoutAndPaddingAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveSafetyGate(null, new LiveGateSettings(TimeSpan.Zero)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveSafetyGate(null, new LiveGateSettings(TimeSpan.FromMilliseconds(250), double.NaN)));
    }
}
