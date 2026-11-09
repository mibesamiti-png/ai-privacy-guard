namespace PrivacyGuard.Core;

public static class MaskProcessor
{
    public static PixelBuffer Apply(PixelBuffer original, IEnumerable<DetectionRecord> regions,
        MaskStyle style, int strength = 16, double padding = 0.2)
    {
        var result = (byte[])original.Bgra.Clone();
        foreach (var region in regions.Where(r => r.Decision == ReviewDecision.Approved))
        {
            var rect = region.Bounds.ToPixels(original.Width, original.Height, padding);
            if (rect.Width == 0 || rect.Height == 0) continue;
            switch (style)
            {
                case MaskStyle.Black:
                    FillBlack(result, original.Width, rect);
                    break;
                case MaskStyle.Blur:
                    BoxBlur(result, original.Width, rect, Math.Clamp(strength / 2, 2, 32));
                    break;
                default:
                    Pixelate(result, original.Width, rect, Math.Clamp(strength, 4, 128));
                    break;
            }
        }
        return new PixelBuffer(original.Width, original.Height, result, copy: false);
    }

    private static void FillBlack(byte[] pixels, int width, PixelRect rect)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
            }
    }

    private static void Pixelate(byte[] pixels, int width, PixelRect rect, int cell)
    {
        int endX = rect.X + rect.Width, endY = rect.Y + rect.Height;
        for (int by = rect.Y; by < endY; by += cell)
        for (int bx = rect.X; bx < endX; bx += cell)
        {
            int x2 = Math.Min(bx + cell, endX), y2 = Math.Min(by + cell, endY);
            long b = 0, g = 0, r = 0; int n = 0;
            for (int y = by; y < y2; y++)
            for (int x = bx; x < x2; x++)
            {
                int i = (y * width + x) * 4;
                b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; n++;
            }
            byte cb = (byte)(b / n), cg = (byte)(g / n), cr = (byte)(r / n);
            for (int y = by; y < y2; y++)
            for (int x = bx; x < x2; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = cb; pixels[i + 1] = cg; pixels[i + 2] = cr;
            }
        }
    }

    // Simple separable box blur confined to the mask region.
    private static void BoxBlur(byte[] pixels, int imageWidth, PixelRect rect, int radius)
    {
        int w = rect.Width, h = rect.Height;
        var source = new byte[checked(w * h * 4)];
        var horizontal = new byte[source.Length];
        for (int y = 0; y < h; y++)
            Array.Copy(pixels, ((rect.Y + y) * imageWidth + rect.X) * 4,
                source, y * w * 4, w * 4);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int xmin = Math.Max(0, x - radius), xmax = Math.Min(w - 1, x + radius);
                int n = xmax - xmin + 1;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int xx = xmin; xx <= xmax; xx++) sum += source[(y * w + xx) * 4 + c];
                    horizontal[(y * w + x) * 4 + c] = (byte)(sum / n);
                }
                horizontal[(y * w + x) * 4 + 3] = 255;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int ymin = Math.Max(0, y - radius), ymax = Math.Min(h - 1, y + radius);
                int n = ymax - ymin + 1;
                int dst = ((rect.Y + y) * imageWidth + rect.X + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int yy = ymin; yy <= ymax; yy++) sum += horizontal[(yy * w + x) * 4 + c];
                    pixels[dst + c] = (byte)(sum / n);
                }
                pixels[dst + 3] = 255;
            }
    }
}
