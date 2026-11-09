using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class VideoMaskPlanTests
{
    [Fact]
    public void Filter_HasBlackBoxAndTimeRange()
    {
        var approved = new VideoMaskCandidate(Guid.NewGuid(), 1_000_000, 2_000_000,
            new NormalizedRect(0.25, 0.25, 0.5, 0.5), ReviewDecision.Approved,
            "MANUAL_REGION", "manual");
        var result = VideoMaskPlan.DrawboxFilter(new[] { approved }, 200, 100, 3_000_000);
        Assert.Contains("x=50:y=25:w=100:h=50", result);
        Assert.Contains("between(t,1,2)", result);
        Assert.Contains("color=black:t=fill", result);
    }

    [Fact]
    public void Filter_IgnoresUnapprovedRegions()
    {
        var box = new NormalizedRect(0.1, 0.1, 0.2, 0.2);
        var ignored = new VideoMaskCandidate(Guid.NewGuid(), 100_000, 200_000,
            box, ReviewDecision.Rejected, "test", "manual");
        var approved = ignored with { Id = Guid.NewGuid(), Decision = ReviewDecision.Approved };
        string result = VideoMaskPlan.DrawboxFilter(new[] { ignored, approved }, 100, 100, 1_000_000);
        Assert.Single(result.Split("drawbox=", StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    [InlineData(10, 5)]
    public void Filter_RejectsInvalidTimes(long startUs, long endUs)
    {
        var candidate = new VideoMaskCandidate(Guid.NewGuid(), startUs, endUs,
            new NormalizedRect(0.1, 0.2, 0.1, 0.1), ReviewDecision.Approved, "x", "manual");
        Assert.Throws<ArgumentException>(() => VideoMaskPlan.DrawboxFilter(new[] { candidate },
            100, 100, 1_000_000));
    }

    [Fact]
    public void Filter_RejectsUnknownOrUnapprovedOutput()
    {
        var candidate = new VideoMaskCandidate(Guid.NewGuid(), 0, 100_000,
            new NormalizedRect(0, 0, 0.2, 0.2), ReviewDecision.Pending, "x", "manual");
        Assert.Throws<InvalidOperationException>(() => VideoMaskPlan.DrawboxFilter(
            new[] { candidate }, 100, 100, 1_000_000));
    }

    [Fact]
    public void Filter_RejectsBoundsOutsideImage()
    {
        var candidate = new VideoMaskCandidate(Guid.NewGuid(), 0, 100_000,
            new NormalizedRect(0.9, 0.2, 0.3, 0.2), ReviewDecision.Approved, "x", "manual");
        Assert.Throws<ArgumentException>(() => VideoMaskPlan.DrawboxFilter(
            new[] { candidate }, 100, 100, 1_000_000));
    }
}
