using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class VideoFrameIndexTests
{
    [Fact]
    public void AdjacentUsesActualVariablePresentationIntervals()
    {
        var index = new VideoFrameIndex(new long[] { 500_000, 0, 85_000, 160_000, 160_000, 335_000 });
        Assert.Equal(5, index.Count);
        Assert.Equal(160_000, index.Closest(155_000));
        Assert.Equal(335_000, index.Adjacent(160_000, 1));
        Assert.Equal(85_000, index.Adjacent(160_000, -1));
        Assert.Equal(0, index.Adjacent(0, -1));
        Assert.Equal(500_000, index.Adjacent(500_000, 1));
    }

    [Fact]
    public void InvalidIndicesAndDirectionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new VideoFrameIndex(Array.Empty<long>()));
        Assert.Throws<ArgumentException>(() => new VideoFrameIndex(new long[] { -1, 0 }));
        var index = new VideoFrameIndex(new long[] { 0, 55_000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Adjacent(0, 0));
    }
}
