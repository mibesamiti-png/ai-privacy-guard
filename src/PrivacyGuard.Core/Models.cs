namespace PrivacyGuard.Core;

public enum ReviewDecision { Pending, Approved, Rejected, Deferred }
public enum MaskStyle { Pixelate, Blur, Black }

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public NormalizedRect Clamp()
    {
        double x1 = Math.Clamp(X, 0, 1);
        double y1 = Math.Clamp(Y, 0, 1);
        double x2 = Math.Clamp(X + Math.Max(0, Width), 0, 1);
        double y2 = Math.Clamp(Y + Math.Max(0, Height), 0, 1);
        return new NormalizedRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    public PixelRect ToPixels(int width, int height, double extraPadding = 0)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var r = Clamp();
        var px = r.Width * Math.Clamp(extraPadding, 0, 1);
        var py = r.Height * Math.Clamp(extraPadding, 0, 1);
        // Guard pixel boundaries against floating-point 6.000000000000001 artifacts.
        const double epsilon = 1e-9;
        int left = Math.Clamp((int)Math.Floor((r.X - px) * width + epsilon), 0, width);
        int top = Math.Clamp((int)Math.Floor((r.Y - py) * height + epsilon), 0, height);
        int right = Math.Clamp((int)Math.Ceiling((r.X + r.Width + px) * width - epsilon), left, width);
        int bottom = Math.Clamp((int)Math.Ceiling((r.Y + r.Height + py) * height - epsilon), top, height);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed record DetectionRecord(
    Guid Id,
    Guid SourceId,
    string Category,
    double? Confidence,
    NormalizedRect Bounds,
    ReviewDecision Decision,
    string Origin,
    DateTimeOffset CreatedAt)
{
    public static DetectionRecord Manual(Guid sourceId, NormalizedRect bounds) =>
        new(Guid.NewGuid(), sourceId, "MANUAL_REGION", null, bounds.Clamp(),
            ReviewDecision.Pending, "manual", DateTimeOffset.UtcNow);
}

public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public PixelBuffer(int width, int height, byte[] bgra, bool copy = true)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Supported maximum: 80 million pixels.");
        if (bgra.LongLength != (long)width * height * 4)
            throw new ArgumentException("Invalid BGRA32 buffer length.", nameof(bgra));
        Width = width;
        Height = height;
        Bgra = copy ? (byte[])bgra.Clone() : bgra;
    }

    public PixelBuffer Crop(NormalizedRect rectangle, double padding = 0.2)
    {
        PixelRect r = rectangle.ToPixels(Width, Height, padding);
        if (r.Width == 0 || r.Height == 0) throw new InvalidOperationException("Empty crop.");
        var result = new byte[checked(r.Width * r.Height * 4)];
        for (int row = 0; row < r.Height; row++)
            Array.Copy(Bgra, ((r.Y + row) * Width + r.X) * 4,
                result, row * r.Width * 4, r.Width * 4);
        return new PixelBuffer(r.Width, r.Height, result, copy: false);
    }
}
