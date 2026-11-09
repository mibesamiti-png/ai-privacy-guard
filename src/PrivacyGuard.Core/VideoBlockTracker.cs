namespace PrivacyGuard.Core;

/// <summary>
/// Conservative, CPU-only image-patch translation estimator. This is template/block matching,
/// NOT optical flow, a learned detector, verified object tracking, or a privacy guarantee.
/// A failure stops the proposal. No unreviewed result is exported automatically.
/// </summary>
public static class VideoBlockTracker
{
    public sealed record Step(bool Accepted, NormalizedRect Rect, double Quality, string Reason);
    public sealed record Proposal(IReadOnlyList<VideoMaskKeyframe> Keys, bool Completed,
        long? BreakUs, string Reason, double AverageQuality);

    private const int Grid = 14;
    private const int MaxDisplacement = 24; // at <=640px tracking resolution, per sampled frame

    public static Step Estimate(PixelBuffer previous, PixelBuffer next, NormalizedRect bounds)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (previous.Width != next.Width || previous.Height != next.Height)
            return new Step(false, bounds, 0, "frame_dimensions_changed");
        if (!VideoMotionTrack.ValidBounds(bounds))
            throw new ArgumentException("Invalid starting bounds.", nameof(bounds));

        var first = Locate(previous, next, bounds, MaxDisplacement);
        if (!first.Valid) return new Step(false, bounds, 0, first.Reason);
        var proposed = new NormalizedRect(bounds.X + first.Dx / (double)previous.Width,
            bounds.Y + first.Dy / (double)previous.Height, bounds.Width, bounds.Height);
        if (!VideoMotionTrack.ValidBounds(proposed))
            return new Step(false, bounds, 0, "left_image_bounds");
        // Forward/backward agreement rejects many distractor/jump matches.
        var reverse = Locate(next, previous, proposed, MaxDisplacement);
        if (!reverse.Valid || Math.Abs(first.Dx + reverse.Dx) > 3 ||
            Math.Abs(first.Dy + reverse.Dy) > 3)
            return new Step(false, bounds, 0, "forward_backward_disagreement");
        double quality = Math.Clamp(1 - first.Error / 80.0, 0, 1);
        return new Step(true, proposed, quality, "ok");
    }

    private sealed record Location(bool Valid, int Dx, int Dy, double Error, string Reason);

    private static Location Locate(PixelBuffer from, PixelBuffer to, NormalizedRect rect, int maxMove)
    {
        var area = rect.ToPixels(from.Width, from.Height, 0.22);
        if (area.Width < 15 || area.Height < 15)
            return new Location(false, 0, 0, double.MaxValue, "region_too_small");
        var xs = new int[Grid * Grid];
        var ys = new int[Grid * Grid];
        var values = new int[Grid * Grid];
        int n = 0;
        double sum = 0, squares = 0;
        for (int j = 0; j < Grid; j++)
        for (int i = 0; i < Grid; i++)
        {
            int x = area.X + (int)((i + 0.5) * area.Width / Grid);
            int y = area.Y + (int)((j + 0.5) * area.Height / Grid);
            if (x < 0 || x >= from.Width || y < 0 || y >= from.Height) continue;
            int v = Luma(from, x, y);
            xs[n] = x; ys[n] = y; values[n] = v; n++;
            sum += v; squares += v * v;
        }
        if (n < 100) return new Location(false, 0, 0, double.MaxValue, "insufficient_patch");
        double variance = squares / n - (sum / n) * (sum / n);
        if (variance < 100) return new Location(false, 0, 0, double.MaxValue, "low_texture");

        double Error(int dx, int dy)
        {
            double total = 0;
            for (int k = 0; k < n; k++)
            {
                int x = xs[k] + dx, y = ys[k] + dy;
                if (x < 0 || y < 0 || x >= to.Width || y >= to.Height)
                    return double.MaxValue;
                total += Math.Abs(values[k] - Luma(to, x, y));
            }
            return total / n;
        }
        int bestX = 0, bestY = 0;
        double best = double.MaxValue;
        // Half-resolution grid search followed by full-pixel refinement.
        for (int dy = -maxMove; dy <= maxMove; dy += 2)
        for (int dx = -maxMove; dx <= maxMove; dx += 2)
        {
            double err = Error(dx, dy);
            if (err < best) { best = err; bestX = dx; bestY = dy; }
        }
        for (int dy = Math.Max(-maxMove, bestY - 2); dy <= Math.Min(maxMove, bestY + 2); dy++)
        for (int dx = Math.Max(-maxMove, bestX - 2); dx <= Math.Min(maxMove, bestX + 2); dx++)
        {
            double err = Error(dx, dy);
            if (err < best) { best = err; bestX = dx; bestY = dy; }
        }
        if (best > 28) return new Location(false, 0, 0, best, "poor_image_match");
        // A second equally good but spatially different match signals ambiguity.
        double second = double.MaxValue;
        for (int dy = -maxMove; dy <= maxMove; dy += 2)
        for (int dx = -maxMove; dx <= maxMove; dx += 2)
        {
            if (Math.Abs(dx - bestX) <= 4 && Math.Abs(dy - bestY) <= 4) continue;
            second = Math.Min(second, Error(dx, dy));
        }
        if (second - best < 1.5)
            return new Location(false, 0, 0, best, "ambiguous_match");
        return new Location(true, bestX, bestY, best, "ok");
    }

    private static int Luma(PixelBuffer frame, int x, int y)
    {
        int index = checked((y * frame.Width + x) * 4);
        var b = frame.Bgra;
        return (29 * b[index] + 150 * b[index + 1] + 77 * b[index + 2]) >> 8;
    }

    /// <summary>Processes a bounded, ordered sequence. On loss it STOPS: no interpolation across gaps.</summary>
    public static async Task<Proposal> ProposeAsync(
        IAsyncEnumerable<(long TimestampUs, PixelBuffer Pixels)> frames,
        NormalizedRect initial, long startUs, long endUs, int frameRate,
        CancellationToken ct)
    {
        if (!VideoMotionTrack.ValidBounds(initial) || startUs < 0 || endUs <= startUs ||
            frameRate is not (8 or 12) || endUs - startUs > 15_000_000)
            throw new ArgumentException("Track <=15 seconds at 8 or 12 fps from valid bounds.");
        var keys = new List<VideoMaskKeyframe>();
        PixelBuffer? old = null;
        NormalizedRect current = initial;
        long last = -1;
        double qualityTotal = 0;
        await foreach (var item in frames.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (item.TimestampUs < startUs || item.TimestampUs >= endUs) continue;
            if (item.TimestampUs <= last)
                return new Proposal(keys, false, item.TimestampUs, "non_monotonic_timestamps", 0);
            if (old is not null)
            {
                var step = Estimate(old, item.Pixels, current);
                if (!step.Accepted)
                    return new Proposal(keys, false, item.TimestampUs, step.Reason,
                        keys.Count > 1 ? qualityTotal / (keys.Count - 1) : 0);
                current = step.Rect;
                qualityTotal += step.Quality;
            }
            keys.Add(new VideoMaskKeyframe(item.TimestampUs, current));
            last = item.TimestampUs;
            old = item.Pixels;
        }
        if (keys.Count < 2)
            return new Proposal(keys, false, null, "insufficient_frames", 0);
        long toleranceUs = 1_000_000L / frameRate + 20_000;
        if (last < endUs - toleranceUs)
            return new Proposal(keys, false, last, "incomplete_interval",
                qualityTotal / (keys.Count - 1));
        return new Proposal(keys, true, null, "ok", qualityTotal / (keys.Count - 1));
    }
}
