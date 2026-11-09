using System.Globalization;
using System.Text;

namespace PrivacyGuard.Core;

/// <summary>FFmpeg sendcmd+drawbox exporter for user-reviewed keyframed regions.</summary>
public sealed record VideoMotionExportScript(string Filter, string CommandText, int CommandCount);

public static class VideoMotionExportPlan
{
    /// <summary>
    /// Commands are emitted at bounded frame-time intervals, with coordinates in source pixels.
    /// Sparse detections are linearly interpolated; this is not reliable exposure detection.
    /// The command filename MUST be a simple ASCII basename located in FFmpeg's working directory.
    /// </summary>
    public static VideoMotionExportScript Build(IReadOnlyList<VideoMaskCandidate> candidates,
        int width, int height, long durationUs, double sampleFps, string commandFileName)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (width <= 0 || height <= 0 || durationUs <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(sampleFps) || sampleFps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(sampleFps));
        if (string.IsNullOrWhiteSpace(commandFileName) ||
            commandFileName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '.'))
            throw new ArgumentException("Command filename must be a simple ASCII basename.", nameof(commandFileName));

        var approved = candidates.Where(c => c.Decision == ReviewDecision.Approved).ToArray();
        if (approved.Length == 0) throw new InvalidOperationException("No approved mask regions.");
        if (approved.Length > 64) throw new NotSupportedException("At most 64 approved masks per export.");
        foreach (var region in approved) VideoMaskPlan.Validate(region, durationUs);

        var filters = new List<string>();
        var changes = new SortedDictionary<long, List<string>>();
        int animated = 0;
        // Evaluate every requested timestamp, independent of constant-/variable-rate input.
        long stepUs = Math.Max(1, (long)Math.Round(1_000_000d / sampleFps));
        for (int i = 0; i < approved.Length; ++i)
        {
            var candidate = approved[i];
            var initial = candidate.BoundsAt(candidate.StartUs).ToPixels(width, height);
            if (initial.Width < 1 || initial.Height < 1)
                throw new ArgumentException("Mask rectangle is too small to export.", nameof(candidates));
            string target = $"drawbox@guard{i}";
            string start = Seconds(candidate.StartUs);
            string end = Seconds(candidate.EndUs);
            filters.Add($"{target}=x={initial.X}:y={initial.Y}:w={initial.Width}:h={initial.Height}:" +
                        $"color=black:t=fill:enable='gte(t,{start})*lt(t,{end})'");
            if (!candidate.IsMoving) continue;
            animated++;
            var last = initial;
            long ticks = (candidate.EndUs - candidate.StartUs) / stepUs;
            if (ticks > 100_000) throw new NotSupportedException("Track is too long to animate at this cadence.");
            // Include only changes in actual pixel coordinates, so stationary ranges are compact.
            for (long k = 1; k <= ticks; ++k)
            {
                long timeUs = candidate.StartUs + k * stepUs;
                if (timeUs >= candidate.EndUs) break;
                var next = candidate.BoundsAt(timeUs).ToPixels(width, height);
                if (next == last) continue;
                if (!changes.TryGetValue(timeUs, out var commands))
                {
                    commands = new List<string>();
                    changes[timeUs] = commands;
                }
                if (next.X != last.X) commands.Add($"{target} x {next.X}");
                if (next.Y != last.Y) commands.Add($"{target} y {next.Y}");
                if (next.Width != last.Width) commands.Add($"{target} w {next.Width}");
                if (next.Height != last.Height) commands.Add($"{target} h {next.Height}");
                last = next;
                if (changes.Count > 50_000) throw new NotSupportedException("Too many mask updates for this export.");
            }
        }
        string filter = string.Join(",", filters);
        var text = new StringBuilder();
        if (animated > 0 && changes.Count > 0)
        {
            foreach (var change in changes)
            {
                text.Append(Seconds(change.Key)).Append(' ')
                    .Append(string.Join(", ", change.Value)).AppendLine(";");
            }
            filter = $"sendcmd=f={commandFileName}," + filter;
        }
        return new VideoMotionExportScript(filter, text.ToString(), changes.Count);
    }

    private static string Seconds(long us) =>
        (us / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
}
