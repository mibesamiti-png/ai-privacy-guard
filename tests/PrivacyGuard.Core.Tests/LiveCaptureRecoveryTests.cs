using System.Runtime.CompilerServices;
using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class LiveCaptureRecoveryTests
{
    private static PixelBuffer Frame()
    {
        var data = new byte[4 * 4 * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = 155; data[i + 1] = 155; data[i + 2] = 155; data[i + 3] = 255;
        }
        return new PixelBuffer(4, 4, data, copy: false);
    }

    private sealed class FakeDetector(bool fail = false) : IExposureDetector
    {
        public string ModelId => "TEST";
        public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken ct)
        {
            if (fail) throw new InvalidOperationException("simulated inference failure");
            return Task.FromResult<IReadOnlyList<DetectionCandidate>>([]);
        }
        public void Dispose() { }
    }

    private sealed class CollectingSink(Action<LiveFrameResult>? onFrame = null) : ILiveFrameSink
    {
        public List<LiveFrameResult> Received { get; } = new();
        public ValueTask WriteAsync(LiveFrameResult result, CancellationToken ct)
        {
            Received.Add(result);
            onFrame?.Invoke(result);
            return ValueTask.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<LiveInputFrame> DisconnectedSource(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        throw new IOException("camera unplugged");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<LiveInputFrame> OneFrameThenDisconnect(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return new LiveInputFrame(0, Frame());
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        throw new IOException("capture lost");
    }

    private static async IAsyncEnumerable<LiveInputFrame> OneFrameOnly(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return new LiveInputFrame(0, Frame());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FailedCaptureRetriesAndLeavesOutputBlackDuringBackoff()
    {
        int attempts = 0, blackouts = 0;
        var phases = new List<CaptureRecoveryPhase>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var gate = new LiveSafetyGate(new FakeDetector());
        gate.EnableExperimentalProcessing();
        var sink = new CollectingSink(_ => cts.Cancel());
        var runner = new LiveCaptureRecovery(
            ct => ++attempts == 1 ? DisconnectedSource(ct) : OneFrameOnly(ct),
            gate, sink, () => blackouts++,
            state => phases.Add(state.Phase),
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));

        await runner.RunAsync(cts.Token);
        Assert.Equal(2, attempts);
        Assert.True(blackouts >= 3);
        Assert.Contains(CaptureRecoveryPhase.Disconnected, phases);
        Assert.Contains(CaptureRecoveryPhase.RetryWait, phases);
        Assert.Single(sink.Received);
        Assert.Equal(LiveFrameDisposition.Screened, sink.Received[0].Disposition);
    }

    [Fact]
    public async Task ReconnectedCameraGetsNewMonotonicSequenceNumbers()
    {
        int attempts = 0, blackouts = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var gate = new LiveSafetyGate(new FakeDetector());
        gate.EnableExperimentalProcessing();
        int sinkCount = 0;
        var sink = new CollectingSink(_ =>
        {
            if (sinkCount++ == 1) cts.Cancel();
        });
        var runner = new LiveCaptureRecovery(
            ct => { attempts++; return OneFrameThenDisconnect(ct); },
            gate, sink, () => blackouts++,
            firstDelay: TimeSpan.FromMilliseconds(10), maxDelay: TimeSpan.FromMilliseconds(20));
        await runner.RunAsync(cts.Token);
        Assert.True(attempts >= 2);
        Assert.Equal(2, sink.Received.Count);
        Assert.Equal(0, sink.Received[0].Sequence);
        Assert.Equal(1, sink.Received[1].Sequence);
        Assert.All(sink.Received, frame => Assert.Equal(LiveFrameDisposition.Screened, frame.Disposition));
    }

    [Fact]
    public async Task DetectorFaultStopsRetriesAndEmitsOnlyBlackout()
    {
        int attempts = 0, blackouts = 0;
        var phases = new List<CaptureRecoveryPhase>();
        var gate = new LiveSafetyGate(new FakeDetector(fail: true));
        gate.EnableExperimentalProcessing();
        var runner = new LiveCaptureRecovery(
            ct => { attempts++; return OneFrameOnly(ct); },
            gate, new CollectingSink(), () => blackouts++,
            e => phases.Add(e.Phase),
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
        await runner.RunAsync(CancellationToken.None);
        Assert.Equal(1, attempts);
        Assert.True(gate.IsFaulted);
        Assert.Contains(CaptureRecoveryPhase.ModelFault, phases);
        Assert.True(blackouts >= 2);
    }

    [Fact]
    public async Task CancellationDuringRetryBackoffExitsCleanly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var gate = new LiveSafetyGate(null);
        var phases = new List<CaptureRecoveryPhase>();
        var runner = new LiveCaptureRecovery(DisconnectedSource, gate,
            new CollectingSink(), () => { },
            e =>
            {
                phases.Add(e.Phase);
                if (e.Phase == CaptureRecoveryPhase.RetryWait) cts.Cancel();
            }, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50));
        await runner.RunAsync(cts.Token);
        Assert.Contains(CaptureRecoveryPhase.Stopped, phases);
    }

    [Fact]
    public void InvalidRetryConfigurationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveCaptureRecovery(DisconnectedSource, new LiveSafetyGate(null),
                new CollectingSink(), () => { },
                firstDelay: TimeSpan.FromMilliseconds(1)));
    }
}
