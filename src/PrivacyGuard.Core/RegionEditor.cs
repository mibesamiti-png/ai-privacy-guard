namespace PrivacyGuard.Core;

public enum ResizeAnchor { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Image-independent rectangle editing. All coordinates are normalized 0..1.</summary>
public static class RegionEditor
{
    public static NormalizedRect Move(NormalizedRect initial, double deltaX, double deltaY)
    {
        var r = initial.Clamp();
        return new NormalizedRect(
            Math.Clamp(r.X + deltaX, 0, 1 - r.Width),
            Math.Clamp(r.Y + deltaY, 0, 1 - r.Height), r.Width, r.Height);
    }

    public static NormalizedRect Resize(NormalizedRect initial, ResizeAnchor corner,
        double pointerX, double pointerY, double minWidth, double minHeight)
    {
        var r = initial.Clamp();
        // If a persisted legacy region is smaller than the UI minimum, do not
        // make Math.Clamp's min larger than its max on a corner drag.
        var minW = Math.Min(Math.Max(minWidth, 0), r.Width);
        var minH = Math.Min(Math.Max(minHeight, 0), r.Height);
        double left = r.X, top = r.Y, right = r.X + r.Width, bottom = r.Y + r.Height;
        switch (corner)
        {
            case ResizeAnchor.TopLeft:
                left = Math.Clamp(pointerX, 0, right - minW);
                top = Math.Clamp(pointerY, 0, bottom - minH);
                break;
            case ResizeAnchor.TopRight:
                right = Math.Clamp(pointerX, left + minW, 1);
                top = Math.Clamp(pointerY, 0, bottom - minH);
                break;
            case ResizeAnchor.BottomLeft:
                left = Math.Clamp(pointerX, 0, right - minW);
                bottom = Math.Clamp(pointerY, top + minH, 1);
                break;
            case ResizeAnchor.BottomRight:
                right = Math.Clamp(pointerX, left + minW, 1);
                bottom = Math.Clamp(pointerY, top + minH, 1);
                break;
        }
        return new NormalizedRect(left, top, right - left, bottom - top).Clamp();
    }

    public static NormalizedRect FromPoints(double x1, double y1, double x2, double y2) =>
        new NormalizedRect(Math.Min(x1, x2), Math.Min(y1, y2),
            Math.Abs(x2 - x1), Math.Abs(y2 - y1)).Clamp();
}
