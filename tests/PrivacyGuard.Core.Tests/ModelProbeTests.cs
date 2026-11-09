using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class ModelProbeTests
{
    private static ModelManifest Manifest() => new()
    {
        ModelId = "probe-test", ModelFile = "fake.onnx", ModelSha256 = new string('a', 64),
        InputWidth = 64, InputHeight = 64,
        Classes = new Dictionary<string, string> { ["0"] = "TEST_REGION" }
    };

    private sealed class FakeDetector(Func<PixelBuffer, IReadOnlyList<DetectionCandidate>> response) : IExposureDetector
    {
        public string ModelId => "probe-test";
        public readonly List<(int width, int height)> Inputs = new();
        public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Inputs.Add((image.Width, image.Height));
            return Task.FromResult(response(image));
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task ProbeUsesTwoSyntheticFramesWithNoUserData()
    {
        var fake = new FakeDetector(_ => Array.Empty<DetectionCandidate>());
        var report = await ModelProbe.RunAsync(Manifest(), fake);
        Assert.Equal("Passed", report.Result);
        Assert.Equal(2, report.Cases.Count);
        Assert.Equal((160, 96), fake.Inputs[0]);
        Assert.Equal((96, 160), fake.Inputs[1]);
        Assert.All(report.Cases, x => Assert.Equal("Passed", x.Status));
        Assert.Contains("not measure", report.Interpretation);
    }

    [Fact]
    public async Task ProbeDoesNotCallInvalidOutputSuccessful()
    {
        var fake = new FakeDetector(_ => new[]
        {
            new DetectionCandidate("TEST_REGION", .9, new NormalizedRect(.9, .9, .2, .2))
        });
        var report = await ModelProbe.RunAsync(Manifest(), fake);
        Assert.Equal("Failed", report.Result);
        Assert.Single(report.Cases);
        Assert.Equal("InvalidDataException", report.Cases[0].FailureKind);
    }

    [Fact]
    public async Task ProbeCatchesDetectorFailureWithoutLeakingPath()
    {
        var fake = new FakeDetector(_ => throw new IOException("secret-path-C:/private/sample.jpg"));
        var report = await ModelProbe.RunAsync(Manifest(), fake);
        var json = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Equal("Failed", report.Result);
        Assert.DoesNotContain("secret-path", json);
        Assert.Contains("IOException", json);
    }

    [Fact]
    public async Task ProbeRespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fake = new FakeDetector(_ => Array.Empty<DetectionCandidate>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ModelProbe.RunAsync(Manifest(), fake, cts.Token));
        Assert.Empty(fake.Inputs);
    }

    [Fact]
    public async Task ProbeRejectsModelIdMismatch()
    {
        var m = Manifest();
        m.ModelId = "other-id";
        var fake = new FakeDetector(_ => Array.Empty<DetectionCandidate>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelProbe.RunAsync(m, fake));
    }
}
