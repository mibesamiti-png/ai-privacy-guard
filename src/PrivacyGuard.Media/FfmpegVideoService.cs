using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using PrivacyGuard.Core;

namespace PrivacyGuard.Media;

public sealed record VideoMetadata(int Width, int Height, long DurationUs,
    double EstimatedFramesPerSecond, bool ApproximateFrameRate)
{
    public string Summary => $"{Width}×{Height} / {DurationUs / 1_000_000.0:0.00}s / " +
        $"{EstimatedFramesPerSecond:0.###} fps (推定{(ApproximateFrameRate ? " / VFRの可能性" : "")})";
}

/// <summary>Local FFmpeg/ffprobe calls. No shell execution; all paths go through ArgumentList.</summary>
public sealed record SampledVideoFrame(long TimestampUs, PixelBuffer Pixels);

public sealed class FfmpegVideoService
{
    private readonly string _ffmpeg;
    private readonly string _ffprobe;
    public FfmpegVideoService(string ffmpeg = "ffmpeg", string ffprobe = "ffprobe")
    {
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
    }

    public async Task<VideoMetadata> ProbeAsync(string path, CancellationToken ct)
    {
        EnsureInput(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        byte[] data = await RunForBytesAsync(_ffprobe, new[]
        {
            "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=width,height,avg_frame_rate,r_frame_rate,duration:format=duration",
            "-of", "json", path
        }, 2_000_000, timeout.Token);
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidOperationException("動画ストリームが見つかりません。");
        var stream = streams[0];
        int width = stream.GetProperty("width").GetInt32();
        int height = stream.GetProperty("height").GetInt32();
        if (width < 1 || height < 1 || (long)width * height > 80_000_000)
            throw new NotSupportedException("Unsupported video dimensions.");
        string? durationText = null;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var d)) durationText = d.GetString();
        if (durationText is null && stream.TryGetProperty("duration", out d)) durationText = d.GetString();
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || seconds <= 0 || seconds > 7 * 24 * 3600)
            throw new NotSupportedException("Duration is missing, invalid or over seven days.");
        double fps = ParseRate(stream, "avg_frame_rate");
        double baseRate = ParseRate(stream, "r_frame_rate");
        if (!double.IsFinite(fps) || fps <= 0 || fps > 240) fps = 30; // UI approximation only, not a frame index.
        return new VideoMetadata(width, height,
            checked((long)Math.Round(seconds * 1_000_000)), fps,
            baseRate > 0 && Math.Abs(baseRate - fps) > 0.01);
    }

    private static double ParseRate(JsonElement stream, string name)
    {
        if (!stream.TryGetProperty(name, out var value)) return 0;
        var parts = (value.GetString() ?? "0").Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return 0;
        double denominator = 1;
        if (parts.Length > 1 && !double.TryParse(parts[1], NumberStyles.Float,
            CultureInfo.InvariantCulture, out denominator)) return 0;
        return denominator == 0 ? 0 : numerator / denominator;
    }


    /// <summary>
    /// Build a bounded, optional index of presentation timestamps. This may be slow on long videos.
    /// Native source timestamps are shifted to the first frame; frame extraction is still a seek.
    /// </summary>
    public async Task<VideoFrameIndex> IndexFrameTimestampsAsync(string path, CancellationToken ct,
        int maxFrames = 300_000)
    {
        EnsureInput(path);
        if (maxFrames < 1 || maxFrames > 500_000) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = Start(_ffprobe, new[] {
            "-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries",
            "frame=best_effort_timestamp_time", "-of", "csv=p=0", path
        });
        try
        {
            var errTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var timestamps = new List<long>();
            double? first = null;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                // ffprobe may emit blank lines or side-data alongside a timestamp.
                string value = line.Split(',')[0].Trim();
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pts) ||
                    !double.IsFinite(pts)) continue;
                first ??= pts;
                long elapsed = checked((long)Math.Round(Math.Max(0, pts - first.Value) * 1_000_000));
                timestamps.Add(elapsed);
                if (timestamps.Count > maxFrames)
                    throw new NotSupportedException($"Frame index exceeds {maxFrames} frames. Use time seeking instead.");
            }
            await process.WaitForExitAsync(timeout.Token);
            string error = await errTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("ffprobe frame indexing failed: " + error[..Math.Min(error.Length, 800)]);
            return new VideoFrameIndex(timestamps);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// Stream downscaled BGRA frames at a regular sampled cadence through FFmpeg's fps filter.
    /// Source video is not materialized in memory. Times are sampling targets, not exact input PTS.
    /// Output MUST be reviewed; a sparse scan cannot certify the intervals between samples.
    /// </summary>
    public async IAsyncEnumerable<SampledVideoFrame> StreamSamplesAsync(
        string path, VideoMetadata video, int samplesPerSecond,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureInput(path);
        // Preflight the same bounded policy as the UI; do not accidentally
        // spin up an unbounded FFmpeg stream when high-density sampling is chosen.
        var budget = VideoScanPlanner.Plan(video.DurationUs, samplesPerSecond,
            video.EstimatedFramesPerSecond);
        const int maxSamples = VideoScanPlanner.MaxSamples;
        double ratio = Math.Min(1.0, 640.0 / Math.Max(video.Width, video.Height));
        int width = Math.Max(2, ((int)Math.Round(video.Width * ratio / 2.0)) * 2);
        int height = Math.Max(2, ((int)Math.Round(video.Height * ratio / 2.0)) * 2);
        int frameBytes = checked(width * height * 4);
        string filters = $"fps={samplesPerSecond}:start_time=0,scale={width}:{height}:flags=fast_bilinear,format=bgra";
        using var process = Start(_ffmpeg, new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-i", path,
            "-map", "0:v:0", "-vf", filters, "-an", "-sn", "-pix_fmt", "bgra",
            "-f", "rawvideo", "pipe:1"
        });
        try
        {
            var errorsTask = process.StandardError.ReadToEndAsync(ct);
            int count = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes = new byte[frameBytes];
                int received = 0;
                while (received < frameBytes)
                {
                    int n = await process.StandardOutput.BaseStream.ReadAsync(
                        bytes.AsMemory(received, frameBytes - received), ct);
                    if (n == 0) break;
                    received += n;
                }
                if (received == 0) break;
                if (received != frameBytes)
                    throw new InvalidDataException("Truncated raw video sample from FFmpeg.");
                if (count >= maxSamples)
                    throw new NotSupportedException("Sample limit exceeded.");
                long timeUs = (long)count * 1_000_000L / samplesPerSecond;
                count++;
                yield return new SampledVideoFrame(timeUs, new PixelBuffer(width, height, bytes, copy: false));
            }
            await process.WaitForExitAsync(ct);
            string errors = await errorsTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("FFmpeg scan failed: " + errors[..Math.Min(errors.Length, 1000)]);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// Bounded segment sampler for experimental block matching (8/12fps, <=15sec).
    /// FFmpeg seeks to the requested start; sample times are approximate grid times,
    /// not authoritative source PTS. Never treat these as proof of frame coverage.
    /// </summary>
    public async IAsyncEnumerable<SampledVideoFrame> StreamTrackingSamplesAsync(
        string path, VideoMetadata video, long startUs, long endUs, int samplesPerSecond,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureInput(path);
        if (video.DurationUs <= 0 || startUs < 0 || endUs <= startUs ||
            endUs > video.DurationUs || endUs - startUs > 15_000_000 ||
            samplesPerSecond is not (8 or 12))
            throw new ArgumentException("Tracking supports 8/12 fps and <=15 second intervals.");
        const int maxSamples = 200;
        double ratio = Math.Min(1.0, 640.0 / Math.Max(video.Width, video.Height));
        int width = Math.Max(2, ((int)Math.Round(video.Width * ratio / 2.0)) * 2);
        int height = Math.Max(2, ((int)Math.Round(video.Height * ratio / 2.0)) * 2);
        int frameBytes = checked(width * height * 4);
        string from = (startUs / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
        string duration = ((endUs - startUs) / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
        string filters = $"fps={samplesPerSecond}:start_time=0,scale={width}:{height}:flags=fast_bilinear,format=bgra";
        using var process = Start(_ffmpeg, new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", from, "-i", path,
            "-t", duration, "-map", "0:v:0", "-vf", filters, "-an", "-sn",
            "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
        });
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            int count = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes = new byte[frameBytes];
                int received = 0;
                while (received < frameBytes)
                {
                    int n = await process.StandardOutput.BaseStream.ReadAsync(
                        bytes.AsMemory(received, frameBytes - received), ct);
                    if (n == 0) break;
                    received += n;
                }
                if (received == 0) break;
                if (received != frameBytes)
                    throw new InvalidDataException("Truncated tracking sample.");
                if (count >= maxSamples)
                    throw new NotSupportedException("Tracking sample limit exceeded.");
                long timeUs = startUs + (long)count * 1_000_000L / samplesPerSecond;
                count++;
                yield return new SampledVideoFrame(timeUs,
                    new PixelBuffer(width, height, bytes, copy: false));
            }
            await process.WaitForExitAsync(ct);
            string error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("FFmpeg tracking sample failed: " +
                    error[..Math.Min(error.Length, 1000)]);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    public async Task<byte[]> ExtractPngAsync(string path, long targetUs, long durationUs, CancellationToken ct)
    {
        EnsureInput(path);
        if (durationUs <= 0) throw new ArgumentOutOfRangeException(nameof(durationUs));
        targetUs = Math.Clamp(targetUs, 0, Math.Max(0, durationUs - 10_000));
        string seconds = (targetUs / 1_000_000d).ToString("0.######", CultureInfo.InvariantCulture);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        // Timeline seeking samples at the requested timestamp. It is NOT exact frame indexing.
        return await RunForBytesAsync(_ffmpeg, new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", seconds,
            "-i", path, "-map", "0:v:0", "-frames:v", "1", "-an", "-sn",
            "-vf", "scale=1280:720:force_original_aspect_ratio=decrease",
            "-f", "image2pipe", "-c:v", "png", "pipe:1"
        }, 24_000_000, timeout.Token);
    }

    public async Task ExportBlackMasksAsync(string inputPath, string outputPath,
        VideoMetadata video, IReadOnlyList<VideoMaskCandidate> candidates, CancellationToken ct,
        bool overwrite = true)
    {
        EnsureInput(inputPath);
        string source = Path.GetFullPath(inputPath);
        string target = Path.GetFullPath(outputPath);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source overwrite is forbidden.");
        if (!target.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be an MP4 file.", nameof(outputPath));
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        // The sendcmd file lives in FFmpeg's working directory; its basename is ASCII-safe.
        string commandName = "apg_motion_" + Guid.NewGuid().ToString("N") + ".txt";
        string commandPath = Path.Combine(directory, commandName);
        double cadence = Math.Clamp(Math.Max(30, video.EstimatedFramesPerSecond), 30, 60);
        var script = VideoMotionExportPlan.Build(candidates, video.Width, video.Height,
            video.DurationUs, cadence, commandName);
        string filters = script.Filter + ",pad=ceil(iw/2)*2:ceil(ih/2)*2";
        string temporary = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}.partial.mp4");
        try
        {
            if (script.CommandText.Length > 0)
                await File.WriteAllTextAsync(commandPath, script.CommandText, new UTF8Encoding(false), ct);
            await RunForExitAsync(_ffmpeg, new[] {
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", source,
                "-map", "0:v:0", "-map", "0:a?", "-vf", filters,
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "18",
                "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
                "-movflags", "+faststart", temporary
            }, ct, directory);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("FFmpeg returned success but no output was produced.");
            File.Move(temporary, target, overwrite: overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(commandPath)) File.Delete(commandPath);
        }
    }

    private static void EnsureInput(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Source media does not exist.", path);
    }

    private static Process Start(string executable, IEnumerable<string> args, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in args) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Cannot launch {executable}.");
            return process;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException($"{executable} was not found. Install FFmpeg / FFprobe and add them to PATH.", ex);
        }
        catch { process.Dispose(); throw; }
    }

    private static async Task<byte[]> RunForBytesAsync(string executable, string[] args,
        int maxBytes, CancellationToken ct)
    {
        using var process = Start(executable, args);
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            using var buffer = new MemoryStream();
            var scratch = new byte[65536];
            int count;
            while ((count = await process.StandardOutput.BaseStream.ReadAsync(scratch, ct)) > 0)
            {
                if (buffer.Length + count > maxBytes)
                    throw new InvalidDataException("Decoded output exceeded the configured memory limit.");
                buffer.Write(scratch, 0, count);
            }
            await process.WaitForExitAsync(ct);
            string errors = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(
                $"{executable} failed: {errors[..Math.Min(1200, errors.Length)]}");
            if (buffer.Length == 0) throw new InvalidOperationException("Frame/output was empty.");
            return buffer.ToArray();
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Already exited. */ }
        }
    }

    private static async Task RunForExitAsync(string executable, string[] args, CancellationToken ct,
        string? workingDirectory = null)
    {
        using var process = Start(executable, args, workingDirectory);
        try
        {
            // Drain stdout and stderr while FFmpeg is running.
            var outTask = process.StandardOutput.ReadToEndAsync(ct);
            var errTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            string errors = await errTask;
            await outTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(
                $"{executable} failed: {errors[..Math.Min(1200, errors.Length)]}");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Already exited. */ }
        }
    }
}
