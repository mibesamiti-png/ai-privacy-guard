using System.Globalization;

namespace PrivacyGuard.Core;

/// <summary>A reviewed region with optional time-varying keyframes. Keys are positions, not AI-certified tracking.</summary>
public sealed record VideoMaskKeyframe(long TimestampUs, NormalizedRect Bounds);

public sealed record VideoMaskCandidate(
    Guid Id, long StartUs, long EndUs, NormalizedRect Bounds,
    ReviewDecision Decision, string Category, string Origin, double? Score = null,
    IReadOnlyList<VideoMaskKeyframe>? Keyframes = null)
{
    public string DecisionLabel => Decision switch
    {
        ReviewDecision.Approved => "隠す",
        ReviewDecision.Rejected => "無視",
        ReviewDecision.Deferred => "保留",
        _ => "未確認"
    };

    public bool IsMoving => Keyframes is { Count: >= 2 };
    public NormalizedRect BoundsAt(long timestampUs) => VideoMotionTrack.BoundsAt(this, timestampUs);

    public string Display => $"{StartUs / 1_000_000d:0.000}–{EndUs / 1_000_000d:0.000}s " +
        $"{Category}  [{DecisionLabel}]" +
        (Score is { } score ? $"  {score:0.00} (未校正)" : "") +
        (IsMoving ? $"  [追従 {Keyframes!.Count}点・補間]" : " [固定]");
}

public static class VideoMaskPlan
{
    public static void Validate(VideoMaskCandidate region, long durationUs)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (durationUs <= 0 || region.StartUs < 0 || region.EndUs <= region.StartUs ||
            region.EndUs > durationUs + 100_000 || string.IsNullOrWhiteSpace(region.Category) ||
            !double.IsFinite(region.Bounds.X) || !double.IsFinite(region.Bounds.Y) ||
            !double.IsFinite(region.Bounds.Width) || !double.IsFinite(region.Bounds.Height) ||
            region.Bounds.X < 0 || region.Bounds.Y < 0 ||
            region.Bounds.Width <= 0 || region.Bounds.Height <= 0 ||
            region.Bounds.X + region.Bounds.Width > 1.000000001 ||
            region.Bounds.Y + region.Bounds.Height > 1.000000001)
            throw new ArgumentException("Invalid video mask time range or rectangle.", nameof(region));
        if (region.Keyframes is { } keys)
        {
            if (keys.Count > 30_000) throw new ArgumentException("Too many keyframes.", nameof(region));
            long previous = -1;
            foreach (var key in keys)
            {
                if (key is null || key.TimestampUs < region.StartUs || key.TimestampUs > region.EndUs ||
                    key.TimestampUs <= previous || !VideoMotionTrack.ValidBounds(key.Bounds))
                    throw new ArgumentException("Keyframes must be ordered, unique, within the review interval and finite.", nameof(region));
                previous = key.TimestampUs;
            }
        }
    }

    /// <summary>Builds FFmpeg drawbox filters. The user explicitly approved every region.</summary>
    public static string DrawboxFilter(IReadOnlyList<VideoMaskCandidate> candidates,
        int width, int height, long durationUs)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var filters = new List<string>();
        foreach (var candidate in candidates)
        {
            if (candidate.Decision != ReviewDecision.Approved) continue;
            Validate(candidate, durationUs);
            var bounds = candidate.Bounds.ToPixels(width, height);
            if (bounds.Width < 1 || bounds.Height < 1) continue;
            string start = (candidate.StartUs / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
            string end = (candidate.EndUs / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
            // Commas in between() are escaped for FFmpeg's filter parser.
            filters.Add($"drawbox=x={bounds.X}:y={bounds.Y}:w={bounds.Width}:h={bounds.Height}:" +
                $"color=black:t=fill:enable='between(t,{start},{end})'");
        }
        if (filters.Count == 0) throw new InvalidOperationException("No approved valid mask regions.");
        return string.Join(",", filters);
    }
}
