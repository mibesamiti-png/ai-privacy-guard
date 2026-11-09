namespace PrivacyGuard.Core;

/// <summary>
/// Computes the *nominal* FFmpeg fps-filter sample budget before starting a scan.
/// This is not frame-perfect coverage, and an event between timestamps can still
/// go unnoticed. Higher rates help only when the source has sufficient frames.
/// </summary>
public sealed record VideoScanBudget(
    int SamplesPerSecond, long EstimatedSamples, int MaxSamples,
    long NominalMaxGapUs, bool SourceFrameRateBelowScanRate)
{
    public string CoverageWarning =>
        $"Sampling is approximately every {NominalMaxGapUs / 1000d:0.#}ms; " +
        "frames between samples are not inspected; VFR and FFmpeg rounding apply." +
        (SourceFrameRateBelowScanRate
            ? " Input FPS is lower than the selected sampling rate; duplicated frames add no coverage."
            : "");
}

public static class VideoScanPlanner
{
    public const int MaxSamples = 30_000;
    private static readonly int[] SupportedRates = [1, 2, 4, 8, 12];

    public static VideoScanBudget Plan(long durationUs, int samplesPerSecond,
        double estimatedSourceFps = double.NaN)
    {
        if (durationUs <= 0 || durationUs > 7L * 24 * 3600 * 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(durationUs));
        if (!SupportedRates.Contains(samplesPerSecond))
            throw new ArgumentOutOfRangeException(nameof(samplesPerSecond),
                "Supported sample rates: 1, 2, 4, 8, 12 fps.");
        // Conservative ceil (+1 for a possible start-boundary sample).
        long estimated = checked((durationUs * samplesPerSecond + 999_999) / 1_000_000 + 1);
        if (estimated > MaxSamples)
            throw new NotSupportedException(
                $"Selected scan is ~{estimated:N0} frames, above the {MaxSamples:N0}-sample limit. " +
                "Reduce fps or scan a shorter file.");
        bool lower = double.IsFinite(estimatedSourceFps) && estimatedSourceFps > 0 &&
            estimatedSourceFps + 0.01 < samplesPerSecond;
        return new VideoScanBudget(samplesPerSecond, estimated, MaxSamples,
            (long)Math.Ceiling(1_000_000d / samplesPerSecond), lower);
    }
}
