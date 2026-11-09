using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class ImageMathTests
{
    [Fact]
    public void ClampPreventsOutOfImageRegion()
    {
        var r = new NormalizedRect(-0.2, 0.8, 0.5, 0.5).ToPixels(100, 100);
        Assert.Equal(new PixelRect(0, 80, 30, 20), r);
    }

    [Fact]
    public void CroppedImageUsesPaddedCoordinates()
    {
        var pixels = Enumerable.Repeat((byte)40, 10 * 10 * 4).ToArray();
        var input = new PixelBuffer(10, 10, pixels);
        var crop = input.Crop(new NormalizedRect(0.2, 0.2, 0.4, 0.4), 0);
        Assert.Equal(4, crop.Width);
        Assert.Equal(4, crop.Height);
        Assert.Equal(4 * 4 * 4, crop.Bgra.Length);
    }

    [Fact]
    public void OnlyApprovedRegionsAreMasked()
    {
        var pixels = Enumerable.Repeat((byte)200, 8 * 8 * 4).ToArray();
        var input = new PixelBuffer(8, 8, pixels);
        var source = Guid.NewGuid();
        var first = DetectionRecord.Manual(source, new NormalizedRect(0, 0, 0.5, 0.5))
            with { Decision = ReviewDecision.Approved };
        var second = DetectionRecord.Manual(source, new NormalizedRect(0.5, 0.5, 0.5, 0.5))
            with { Decision = ReviewDecision.Rejected };
        var result = MaskProcessor.Apply(input, [first, second], MaskStyle.Black, padding: 0);
        Assert.Equal((byte)0, result.Bgra[0]);
        Assert.Equal((byte)200, result.Bgra[((7 * 8 + 7) * 4)]);
        Assert.Equal((byte)200, input.Bgra[0]);
    }

    [Fact]
    public void PixelatedResultKeepsOriginalUnchanged()
    {
        var bytes = new byte[16 * 16 * 4];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var i = (y * 16 + x) * 4;
            bytes[i] = (byte)(x * 10); bytes[i+1] = (byte)(y * 10); bytes[i+2] = 100;
            bytes[i+3] = 255;
        }
        var original = new PixelBuffer(16, 16, bytes);
        var approved = DetectionRecord.Manual(Guid.NewGuid(), new NormalizedRect(0,0,1,1))
            with { Decision = ReviewDecision.Approved };
        var masked = MaskProcessor.Apply(original, [approved], MaskStyle.Pixelate, 8, 0);
        Assert.NotEqual(original.Bgra[0], masked.Bgra[0]);
        Assert.Equal(bytes, original.Bgra);
    }

    [Fact]
    public void InvalidBufferLengthThrows()
    {
        Assert.Throws<ArgumentException>(() => new PixelBuffer(5, 5, new byte[10]));
    }
}
