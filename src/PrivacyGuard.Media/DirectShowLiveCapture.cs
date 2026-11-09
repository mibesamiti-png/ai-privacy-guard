using System.Diagnostics;
using System.Runtime.CompilerServices;
using PrivacyGuard.Core;

namespace PrivacyGuard.Media;

/// <summary>
/// Experimental Windows DirectShow camera capture. Frames are raw at this
/// boundary. Never connect this source directly to OBS or an external sink;
/// route it through LiveSafetyGate and LiveFramePipeline first.
/// </summary>
public sealed class DirectShowLiveCapture
{
    private readonly string _ffmpeg;

    public DirectShowLiveCapture(string ffmpeg = "ffmpeg")
    {
        if (string.IsNullOrWhiteSpace(ffmpeg))
            throw new ArgumentException("FFmpeg executable is required.", nameof(ffmpeg));
        _ffmpeg = ffmpeg;
    }

    public async IAsyncEnumerable<LiveInputFrame> CaptureAsync(
        string cameraName, int width, int height, int fps,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow capture requires Windows.");
        if (string.IsNullOrWhiteSpace(cameraName) ||
            cameraName.Length > 256 ||
            cameraName.Any(c => char.IsControl(c)))
            throw new ArgumentException("Invalid camera name.", nameof(cameraName));
        if (width is < 16 or > 1920 || height is < 16 or > 1080 ||
            (long)width * height > 1920 * 1080)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (fps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(fps));

        int frameBytes = checked(width * height * 4);
        var startInfo = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-f", "dshow", "-i", "video=" + cameraName,
            "-map", "0:v:0",
            "-vf", $"fps={fps},scale={width}:{height}:flags=fast_bilinear,format=bgra",
            "-an", "-sn", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
        }) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Could not start FFmpeg capture.");

        // Drain stderr to avoid deadlock if the decoder reports warnings.
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        long nextSequence = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = new byte[frameBytes];
                int count = 0;
                while (count < frameBytes)
                {
                    int read = await process.StandardOutput.BaseStream.ReadAsync(
                        bytes.AsMemory(count, frameBytes - count), ct);
                    if (read == 0) break;
                    count += read;
                }

                if (count == 0) break;
                if (count != frameBytes)
                    throw new InvalidDataException("FFmpeg returned a truncated camera frame.");
                yield return new LiveInputFrame(nextSequence++,
                    new PixelBuffer(width, height, bytes, copy: false));
            }

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                string error = await stderrTask;
                throw new InvalidOperationException(
                    "FFmpeg camera capture ended: " + error[..Math.Min(600, error.Length)]);
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync(CancellationToken.None); }
            catch (InvalidOperationException) { }
        }
    }
}
