using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class VideoEventGroupingTests
{
    private static TimedDetection D(long time, double x = .1, int? track = null,
        string category = "TEST_REGION") =>
        new(time, category, .85, new NormalizedRect(x, .15, .25, .2), track);

    [Fact]
    public void GroupsAdjacentDetectionsByOverlap()
    {
        var result = VideoEventGrouping.Group(new[] { D(200_000, .12), D(0), D(100_000, .11) });
        Assert.Single(result);
        Assert.Equal(0, result[0].StartUs);
        Assert.Equal(200_000, result[0].EndUs);
        Assert.Equal(3, result[0].Detections.Count);
    }

    [Fact]
    public void SimultaneousDistinctRegionsStaySeparate()
    {
        var result = VideoEventGrouping.Group(new[] { D(100_000, .1), D(100_000, .6) });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SplitsEventsAfterLargeGap()
    {
        var result = VideoEventGrouping.Group(new[] { D(0), D(100_000), D(400_001) });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DoesNotMergeDifferentTrackIds()
    {
        var result = VideoEventGrouping.Group(new[] { D(10_000, .1, 1), D(20_000, .1, 2) });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DoesNotMergeDifferentCategories()
    {
        var result = VideoEventGrouping.Group(new[] { D(10_000), D(20_000, category: "OTHER") });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void RejectsInvalidPresentationTimesAndRects()
    {
        Assert.Throws<ArgumentException>(() => VideoEventGrouping.Group(new[] { D(-1) }));
        Assert.Throws<ArgumentException>(() => VideoEventGrouping.Group(new[] { D(1, x: .9) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoEventGrouping.Group(new[] { D(0) }, 0));
    }
}
