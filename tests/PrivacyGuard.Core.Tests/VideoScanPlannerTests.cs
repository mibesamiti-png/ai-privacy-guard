using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class VideoScanPlannerTests
{
    [Theory]
    [InlineData(1, 60, 61, 1_000_000)]
    [InlineData(2, 60, 121, 500_000)]
    [InlineData(4, 60, 241, 250_000)]
    [InlineData(8, 60, 481, 125_000)]
    [InlineData(12, 60, 721, 83_334)]
    public void SupportedProfilesReturnBoundedNominalBudgets(
        int fps, int seconds, long expectedSamples, long expectedGapUs)
    {
        var budget = VideoScanPlanner.Plan(seconds * 1_000_000L, fps, 30);
        Assert.Equal(fps, budget.SamplesPerSecond);
        Assert.Equal(expectedSamples, budget.EstimatedSamples);
        Assert.Equal(VideoScanPlanner.MaxSamples, budget.MaxSamples);
        Assert.Equal(expectedGapUs, budget.NominalMaxGapUs);
        Assert.False(budget.SourceFrameRateBelowScanRate);
        Assert.Contains("not inspected", budget.CoverageWarning);
    }

    [Fact]
    public void LongHighDensityScanIsRejectedBeforeAnyFramesAreRead()
    {
        Assert.Throws<NotSupportedException>(() =>
            VideoScanPlanner.Plan(3000L * 1_000_000, 12));
        Assert.Equal(24_001, VideoScanPlanner.Plan(3000L * 1_000_000, 8).EstimatedSamples);
    }

    [Fact]
    public void LowerSourceFpsIsExplicitlyReported()
    {
        var plan = VideoScanPlanner.Plan(10_000_000, 12, estimatedSourceFps: 5);
        Assert.True(plan.SourceFrameRateBelowScanRate);
        Assert.Contains("duplicated frames", plan.CoverageWarning);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    public void UnsupportedRatesNeverPassValidation(int fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoScanPlanner.Plan(10_000_000, fps));
    }

    [Fact]
    public void BadDurationCannotGenerateSamplingBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoScanPlanner.Plan(0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoScanPlanner.Plan(-500, 12));
    }
}
