using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class RegionEditorTests
{
    [Fact]
    public void MovingRegionPreservesItsSizeAndClampsToImage()
    {
        var initial = new NormalizedRect(0.2, 0.2, 0.3, 0.4);
        var actual = RegionEditor.Move(initial, 3, -1);
        Assert.Equal(0.7, actual.X, 10);
        Assert.Equal(0, actual.Y, 10);
        Assert.Equal(0.3, actual.Width, 10);
        Assert.Equal(0.4, actual.Height, 10);
    }

    [Theory]
    [InlineData(ResizeAnchor.TopLeft, -1, -1, 0, 0, 0.8, 0.7)]
    [InlineData(ResizeAnchor.BottomRight, 2, 2, 0.2, 0.3, 0.8, 0.7)]
    [InlineData(ResizeAnchor.TopRight, 0.21, 0.69, 0.2, 0.65, 0.05, 0.05)]
    public void ResizingPreventsOutOfBoundsAndInvertedRectangles(ResizeAnchor anchor,
        double x, double y, double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var initial = new NormalizedRect(0.2, 0.3, 0.6, 0.4);
        var result = RegionEditor.Resize(initial, anchor, x, y, 0.05, 0.05);
        Assert.Equal(expectedX, result.X, 5);
        Assert.Equal(expectedY, result.Y, 5);
        Assert.Equal(expectedWidth, result.Width, 5);
        Assert.Equal(expectedHeight, result.Height, 5);
    }

    [Fact]
    public void DraggingBackwardsStillCreatesPositiveRectangle()
    {
        var rect = RegionEditor.FromPoints(0.7, 0.7, 0.2, 0.1);
        Assert.Equal(0.2, rect.X, 5);
        Assert.Equal(0.1, rect.Y, 5);
        Assert.Equal(0.5, rect.Width, 5);
        Assert.Equal(0.6, rect.Height, 5);
    }

    [Fact]
    public void ThumbnailContainsOnlySamplesFromTargetRegionAndDoesNotModifySource()
    {
        const int width = 10, height = 10;
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = (y * width + x) * 4;
            bytes[index] = (byte)(x * 10);
            bytes[index + 1] = (byte)(y * 10);
            bytes[index + 3] = 255;
        }
        var original = new PixelBuffer(width, height, bytes);
        var thumb = PreviewProcessor.Thumbnail(original, new NormalizedRect(0.2, 0.2, 0.4, 0.4),
            2, 2, padding: 0);
        Assert.Equal(2, thumb.Width);
        Assert.Equal(2, thumb.Height);
        Assert.Equal((byte)30, thumb.Bgra[0]);
        Assert.Equal((byte)30, thumb.Bgra[1]);
        Assert.Equal(bytes, original.Bgra);
    }

    [Fact]
    public void PreviewDimensionsDoNotUpscaleOrExceedLimits()
    {
        var source = new PixelBuffer(100, 50, new byte[100 * 50 * 4]);
        var reduced = PreviewProcessor.Downsample(source, 20, 20);
        Assert.Equal(20, reduced.Width);
        Assert.Equal(10, reduced.Height);
        Assert.Equal(100, PreviewProcessor.Downsample(source, 200, 200).Width);
    }

    [Fact]
    public void ChangedRegionMasksNewLocationButNotOldOne()
    {
        var original = new PixelBuffer(10, 10, Enumerable.Repeat((byte)200, 10 * 10 * 4).ToArray());
        var record = DetectionRecord.Manual(Guid.NewGuid(), new NormalizedRect(0, 0, 0.2, 0.2))
            with { Decision = ReviewDecision.Approved, Bounds = new NormalizedRect(0.8, 0.8, 0.2, 0.2) };
        var result = MaskProcessor.Apply(original, [record], MaskStyle.Black, padding: 0);
        Assert.Equal((byte)200, result.Bgra[0]);
        Assert.Equal((byte)0, result.Bgra[((9 * 10 + 9) * 4)]);
    }
}
