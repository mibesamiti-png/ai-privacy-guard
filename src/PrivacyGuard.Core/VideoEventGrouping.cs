namespace PrivacyGuard.Core;

/// <summary>R3 preparation: timestamps are presentation times in microseconds (not frame indices).</summary>
public sealed record TimedDetection(long TimestampUs, string Category, double Score,
    NormalizedRect Bounds, int? TrackId = null);
public sealed record VideoDetectionEvent(string Category, int? TrackId, long StartUs,
    long EndUs, IReadOnlyList<TimedDetection> Detections);

public static class VideoEventGrouping
{
    /// <summary>
    /// Consolidates observations into candidate review intervals. Separate simultaneous
    /// objects are not merged. This is NOT a tracker: model-provided TrackId is optional.
    /// </summary>
    public static IReadOnlyList<VideoDetectionEvent> Group(
        IEnumerable<TimedDetection> detections,
        long maxGapUs = 250_000, double minIou = 0.20)
    {
        ArgumentNullException.ThrowIfNull(detections);
        if (maxGapUs <= 0) throw new ArgumentOutOfRangeException(nameof(maxGapUs));
        if (!double.IsFinite(minIou) || minIou is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minIou));
        var sorted = detections.OrderBy(d => d.TimestampUs).ToArray();
        foreach (var d in sorted)
        {
            if (d.TimestampUs < 0 || string.IsNullOrWhiteSpace(d.Category) ||
                !double.IsFinite(d.Score) || d.Score is < 0 or > 1 ||
                !ValidBounds(d.Bounds))
                throw new ArgumentException("Invalid timed detection.", nameof(detections));
        }
        var groups = new List<MutableGroup>();
        foreach (var detection in sorted)
        {
            MutableGroup? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var g in groups)
            {
                if (g.Category != detection.Category ||
                    g.Last.TimestampUs >= detection.TimestampUs ||
                    detection.TimestampUs - g.Last.TimestampUs > maxGapUs) continue;
                if (g.TrackId is not null || detection.TrackId is not null)
                {
                    if (g.TrackId != detection.TrackId) continue;
                }
                double overlap = ModelTensorProcessor.IntersectionOverUnion(
                    g.Last.Bounds, detection.Bounds);
                if (overlap < minIou) continue;
                if (overlap > bestScore)
                {
                    best = g;
                    bestScore = overlap;
                }
            }
            if (best is null)
                groups.Add(new MutableGroup(detection));
            else
                best.Detections.Add(detection);
        }
        return groups.OrderBy(g => g.Detections[0].TimestampUs)
            .Select(g => new VideoDetectionEvent(g.Category, g.TrackId,
                g.Detections[0].TimestampUs, g.Last.TimestampUs, g.Detections.ToArray()))
            .ToArray();
    }

    private static bool ValidBounds(NormalizedRect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) &&
        double.IsFinite(r.Width) && double.IsFinite(r.Height) &&
        r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
        r.X + r.Width <= 1.0000001 && r.Y + r.Height <= 1.0000001;

    private sealed class MutableGroup(TimedDetection first)
    {
        public string Category => Detections[0].Category;
        public int? TrackId => Detections[0].TrackId;
        public List<TimedDetection> Detections { get; } = new() { first };
        public TimedDetection Last => Detections[^1];
    }
}
