using System.Runtime.CompilerServices;
using PrivacyGuard.AI;
using PrivacyGuard.Core;
using PrivacyGuard.LiveObs;
using PrivacyGuard.Media;

namespace PrivacyGuard.LiveBridge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        IExposureDetector? detector = null;
        try
        {
            bool demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
            string? camera = ReadOption(args, "--camera");
            string? manifestPath = ReadOption(args, "--manifest");
            bool allowUnverified = args.Contains("--experimental-unverified-model", StringComparer.OrdinalIgnoreCase);
            if (demo == (camera is not null))
                throw new ArgumentException("Specify exactly one of --demo or --camera NAME.");
            if (manifestPath is not null && (demo || !allowUnverified))
                throw new ArgumentException("A model requires --camera plus --experimental-unverified-model.");
            if (allowUnverified && manifestPath is null)
                throw new ArgumentException("--experimental-unverified-model requires --manifest.");
            int width = ReadInt(args, "--width", 640, 16, 1920);
            int height = ReadInt(args, "--height", 360, 16, 1080);
            int fps = ReadInt(args, "--fps", 10, 1, 30);
            int port = ReadInt(args, "--port", 18731, 1024, 65535);
            string? audioInput = ReadOption(args, "--obs-audio-input");
            string? offsetText = ReadOption(args, "--obs-audio-offset-ms");
            if ((audioInput is null) != (offsetText is null))
                throw new ArgumentException("--obs-audio-input and --obs-audio-offset-ms must be used together.");
            int? audioOffset = null;
            if (audioInput is not null)
            {
                if (!int.TryParse(offsetText, out int requestedOffset))
                    throw new ArgumentException("Invalid OBS audio offset in milliseconds.");
                ObsAudioSyncProtocol.Validate(audioInput, requestedOffset);
                audioOffset = requestedOffset;
            }

            if (demo)
                detector = new SyntheticDemoDetector();
            else if (manifestPath is not null)
            {
                var (manifest, modelPath) = ModelManifest.LoadAndValidate(manifestPath);
                detector = new OnnxExposureDetector(manifest, modelPath);
            }

            var gate = new LiveSafetyGate(detector, new LiveGateSettings(TimeSpan.FromMilliseconds(250)));
            if (detector is not null) gate.EnableExperimentalProcessing();
            var watchdog = new LiveOutputWatchdog(width, height, TimeSpan.FromMilliseconds(600));
            await using var bridge = new LocalObsBrowserSource(watchdog, port);
            await bridge.StartAsync(stop.Token);
            Console.WriteLine("AI Privacy Guard — R5.1 experimental OBS Browser Source bridge");
            Console.WriteLine("Use OBS → Sources → Browser, then URL:");
            Console.WriteLine(bridge.BrowserSourceUrl);
            Console.WriteLine($"Recommended Browser Source size: {width} x {height}");
            Console.WriteLine(demo
                ? "SYNTHETIC DEMO ONLY. No personal video is used."
                : detector is null
                    ? "NO VALIDATED DETECTOR: All camera frames stay BLACK."
                    : "UNVERIFIED DETECTOR: Do not rely on this to prevent accidental exposure.");
            Console.WriteLine("This bridge is a prototype. OBS/renderer failure may freeze the image.");
            if (audioOffset is not null)
            {
                try
                {
                    await new ObsAudioSyncClient().SetInputOffsetAsync(audioInput!, audioOffset.Value,
                        Environment.GetEnvironmentVariable("APG_OBS_WS_PASSWORD"), stop.Token);
                    Console.WriteLine($"OBS audio input offset set to {audioOffset.Value} ms for {audioInput}.");
                    Console.WriteLine("Audio offset is manually supplied; it is NOT auto-calibrated.");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Console.Error.WriteLine("OBS audio offset unchanged: " + ex.GetType().Name +
                        ". Check OBS WebSocket configuration and input name.");
                }
            }
            Console.WriteLine("Press Ctrl+C to exit.");
            var recovery = new LiveCaptureRecovery(
                token => demo
                    ? SyntheticFrames(width, height, fps, token)
                    : new DirectShowLiveCapture().CaptureAsync(camera!, width, height, fps, token),
                gate, watchdog, watchdog.ForceBlackout,
                status => Console.WriteLine($"Capture: {status.Phase} (retry {status.Attempt}) - {status.Detail}"));
            try
            {
                await recovery.RunAsync(stop.Token);
                // Keep the local output endpoint serving BLACK on terminal
                // detector failure until the operator explicitly shuts down.
                if (!stop.IsCancellationRequested)
                {
                    Console.WriteLine("Capture stopped; local OBS output remains BLACK. Press Ctrl+C.");
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
                    catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Capture unavailable; output is black: " + ex.GetType().Name);
                watchdog.ForceBlackout();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
                catch (OperationCanceledException) { }
            }
            finally { watchdog.ForceBlackout(); }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { detector?.Dispose(); }
    }

    private static async IAsyncEnumerable<LiveInputFrame> SyntheticFrames(
        int width, int height, int fps, [EnumeratorCancellation] CancellationToken ct)
    {
        for (long n = 0; ; n++)
        {
            ct.ThrowIfCancellationRequested();
            var pixels = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                pixels[p] = 40;
                pixels[p + 1] = (byte)(25 + (x * 25 / width));
                pixels[p + 2] = 50;
                pixels[p + 3] = 255;
            }
            int left = (int)((n * 5) % Math.Max(1, width - width / 5));
            for (int y = height / 3; y < height * 2 / 3; y++)
            for (int x = left; x < Math.Min(width, left + width / 5); x++)
            {
                int p = (y * width + x) * 4;
                pixels[p] = 180; pixels[p + 1] = 220; pixels[p + 2] = 80;
            }
            yield return new LiveInputFrame(n, new PixelBuffer(width, height, pixels, copy: false));
            await Task.Delay(TimeSpan.FromSeconds(1.0 / fps), ct);
        }
    }

    private sealed class SyntheticDemoDetector : IExposureDetector
    {
        public string ModelId => "SYNTHETIC-DEMO-NOT-EXPOSURE-DETECTION";
        public void Dispose() { }
        public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<DetectionCandidate> result =
            [
                new DetectionCandidate("SYNTHETIC_DEMO", 1,
                    new NormalizedRect(0.38, 0.36, 0.24, 0.28))
            ];
            return Task.FromResult(result);
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        if (i + 1 == args.Length || args[i + 1].StartsWith("--"))
            throw new ArgumentException("Missing value after " + name);
        return args[i + 1];
    }

    private static int ReadInt(string[] args, string option, int defaultValue, int min, int max)
    {
        string? value = ReadOption(args, option);
        if (value is null) return defaultValue;
        if (!int.TryParse(value, out int number) || number < min || number > max)
            throw new ArgumentException($"{option} must be between {min} and {max}.");
        return number;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
AIPrivacyGuard.LiveBridge — experimental OBS Browser Source
  --demo                          Synthetic frames; does not use a camera
  --camera "Device name"          Windows FFmpeg DirectShow camera (BLACK without model)
  --manifest "path.json"          Compatible ONNX model manifest, camera mode only
  --experimental-unverified-model Explicitly allow unverified detector output
  --width 640 --height 360        Output dimensions
  --fps 10                        Camera/demo frame rate
  --port 18731                    Localhost Browser Source port
  --obs-audio-input "Mic/Aux"      Optional OBS audio input to adjust
  --obs-audio-offset-ms 350       Manual OBS audio sync offset in milliseconds
                                  Password via APG_OBS_WS_PASSWORD environment variable

DO NOT use this experimental implementation to guarantee privacy in live streams.
""");
    }
}
