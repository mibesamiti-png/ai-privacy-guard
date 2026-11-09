namespace PrivacyGuard.Core;

/// <summary>
/// Linear interpolation of human-reviewable mask rectangles at presentation timestamps.
/// This is NOT automatic motion estimation. Empty/one-key tracks are treated as fixed.
/// </summary>
public static class VideoMotionTrack
{
    public static bool ValidBounds(NormalizedRect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height) &&
        r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
        r.X + r.Width <= 1.000000001 && r.Y + r.Height <= 1.000000001;

    public static NormalizedRect BoundsAt(VideoMaskCandidate candidate, long timestampUs)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var keys = candidate.Keyframes;
        if (keys is null || keys.Count < 2) return candidate.Bounds;
        if (timestampUs <= keys[0].TimestampUs) return keys[0].Bounds;
        if (timestampUs >= keys[^1].TimestampUs) return keys[^1].Bounds;
        // Binary search avoids traversing long tracks on every exported frame.
        int low = 0, high = keys.Count - 1;
        while (low + 1 < high)
        {
            int mid = low + (high - low) / 2;
            if (keys[mid].TimestampUs <= timestampUs) low = mid;
            else high = mid;
        }
        var left = keys[low];
        var right = keys[high];
        double alpha = (timestampUs - left.TimestampUs) / (double)(right.TimestampUs - left.TimestampUs);
        double Mix(double a, double b) => a + (b - a) * alpha;
        return new NormalizedRect(
            Mix(left.Bounds.X, right.Bounds.X), Mix(left.Bounds.Y, right.Bounds.Y),
            Mix(left.Bounds.Width, right.Bounds.Width), Mix(left.Bounds.Height, right.Bounds.Height));
    }

    public static IReadOnlyList<VideoMaskKeyframe> Upsert(
        IReadOnlyList<VideoMaskKeyframe>? existing, VideoMaskKeyframe value)
    {
        var list = (existing ?? Array.Empty<VideoMaskKeyframe>()).
            Where(k => k.TimestampUs != value.TimestampUs).Append(value).
            OrderBy(k => k.TimestampUs).ToArray();
        return list;
    }
}
