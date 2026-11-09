using System.Diagnostics;

namespace PrivacyGuard.Core;

/// <summary>
/// In-memory interoperability probe. Uses synthetic pixels only: a PASS means
/// that model execution / output decoding worked, NOT that detection is correct.
/// </summary>
public static class ModelProbe
{
    public sealed record ProbeCase(string Pattern, int SourceWidth, int SourceHeight,
        double ElapsedMs, int CandidateCount, string Status, string? FailureKind);

    public sealed record ProbeReport(int SchemaVersion, string ModelId, string ModelSha256,
        DateTimeOffset ExecutedUtc, string Result, string Interpretation,
        IReadOnlyList<ProbeCase> Cases);

    public static async Task<ProbeReport> RunAsync(
        ModelManifest manifest, IExposureDetector detector, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(detector);
        manifest.Validate();
        if (!string.Equals(manifest.ModelId, detector.ModelId, StringComparison.Ordinal))
            throw new InvalidOperationException("Detector does not match manifest model ID.");

        var cases = new List<ProbeCase>();
        foreach (var (name, width, height) in new[]
        {
            ("synthetic_wide", 160, 96), ("synthetic_tall", 96, 160)
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = SyntheticFrame(width, height);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var candidates = await detector.DetectAsync(image, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (candidates is null || candidates.Any(c =>
                    string.IsNullOrWhiteSpace(c.Category) || !double.IsFinite(c.Score) || c.Score is < 0 or > 1 ||
                    !double.IsFinite(c.Bounds.X) || !double.IsFinite(c.Bounds.Y) ||
                    !double.IsFinite(c.Bounds.Width) || !double.IsFinite(c.Bounds.Height) ||
                    c.Bounds.Width <= 0 || c.Bounds.Height <= 0 ||
                    c.Bounds.X < 0 || c.Bounds.Y < 0 ||
                    c.Bounds.X + c.Bounds.Width > 1.0000001 ||
                    c.Bounds.Y + c.Bounds.Height > 1.0000001))
                    throw new InvalidDataException("Detector output includes an invalid region.");
                stopwatch.Stop();
                cases.Add(new ProbeCase(name, width, height,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), candidates.Count, "Passed", null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                // Only a type name is serialized, never a local path, media or raw exception text.
                cases.Add(new ProbeCase(name, width, height,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), 0, "Failed", ex.GetType().Name));
                break;
            }
        }
        return new ProbeReport(1, manifest.ModelId, manifest.ModelSha256.ToUpperInvariant(),
            DateTimeOffset.UtcNow, cases.Count == 2 && cases.All(x => x.Status == "Passed") ? "Passed" : "Failed",
            "Synthetic runtime/shape smoke test only. This does not measure exposure detection accuracy, false negatives, or livestream safety.",
            cases);
    }

    private static PixelBuffer SyntheticFrame(int width, int height)
    {
        var bgra = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bgra[i] = (byte)((x * 3 + y) & 255);
            bgra[i + 1] = (byte)((y * 2) & 255);
            bgra[i + 2] = (byte)((x + y * 2) & 255);
            bgra[i + 3] = 255;
        }
        return new PixelBuffer(width, height, bgra, copy: false);
    }
}
