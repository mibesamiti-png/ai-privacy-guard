namespace PrivacyGuard.Core;

/// <summary>Memory-bounded previews; never modifies the source buffer.</summary>
public static class PreviewProcessor
{
    public static PixelBuffer Downsample(PixelBuffer source, int maxWidth = 960, int maxHeight = 720) =>
        Sample(source, new PixelRect(0, 0, source.Width, source.Height), maxWidth, maxHeight);

    public static PixelBuffer Thumbnail(PixelBuffer source, NormalizedRect bounds,
        int maxWidth = 112, int maxHeight = 84, double padding = 0.2)
    {
        var rect = bounds.ToPixels(source.Width, source.Height, padding);
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("Cannot preview an empty detection region.");
        return Sample(source, rect, maxWidth, maxHeight);
    }

    private static PixelBuffer Sample(PixelBuffer source, PixelRect region, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        double scale = Math.Min(1, Math.Min(maxWidth / (double)region.Width, maxHeight / (double)region.Height));
        int w = Math.Max(1, (int)Math.Round(region.Width * scale));
        int h = Math.Max(1, (int)Math.Round(region.Height * scale));
        var bytes = new byte[checked(w * h * 4)];
        for (int y = 0; y < h; y++)
        {
            int srcY = region.Y + Math.Min(region.Height - 1,
                (int)((y + 0.5) * region.Height / h));
            for (int x = 0; x < w; x++)
            {
                int srcX = region.X + Math.Min(region.Width - 1,
                    (int)((x + 0.5) * region.Width / w));
                Buffer.BlockCopy(source.Bgra, (srcY * source.Width + srcX) * 4,
                    bytes, (y * w + x) * 4, 4);
            }
        }
        return new PixelBuffer(w, h, bytes, copy: false);
    }
}
