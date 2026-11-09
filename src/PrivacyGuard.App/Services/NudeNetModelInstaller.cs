using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

/// <summary>
/// Opt-in model download to the current user's private LocalAppData folder.
/// Nothing is uploaded, and model weights are never bundled with this public
/// repository. This SHA-256 is a pin for a specific third-party 320n ONNX.
/// </summary>
public static class NudeNetModelInstaller
{
    private const string FileName = "320n.onnx";
    private const string ManifestName = "nudenet320.manifest.json";

    // Third-party pinned NudeNet ONNX mirror. Distribution/licensing remains
    // the responsibility of the upstream rights holders and the end user.
    public const string SourceUrl = NudeNetPreset.PinnedUrl;
    public const string ExpectedSha256 = NudeNetPreset.ModelSha256;

    public static string InstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIPrivacyGuard", "models", "nudenet320");
    public static string ManifestPath => Path.Combine(InstallFolder, ManifestName);

    public static async Task<string> InstallAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(InstallFolder);
        string destination = Path.Combine(InstallFolder, FileName);

        if (!await CheckExistingAsync(destination, ct))
        {
            string tempPath = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AIPrivacyGuard/1.0");
                using var response = await http.GetAsync(SourceUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                const long maxSize = 80L * 1024 * 1024;
                if (response.Content.Headers.ContentLength is > maxSize)
                    throw new InvalidDataException("Model is larger than expected.");
                long total = response.Content.Headers.ContentLength ?? 0;
                long readBytes = 0;
                using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var sink = File.Create(tempPath);
                byte[] buffer = new byte[128 * 1024];
                int count;
                while ((count = await source.ReadAsync(buffer, ct)) != 0)
                {
                    readBytes += count;
                    if (readBytes > maxSize)
                        throw new InvalidDataException("Model exceeds the maximum allowed size.");
                    await sink.WriteAsync(buffer.AsMemory(0, count), ct);
                    if (total > 0) progress?.Report(Math.Clamp(readBytes / (double)total, 0, 1));
                }
                await sink.FlushAsync(ct);
                sink.Close();

                if (!await CheckExistingAsync(tempPath, ct))
                    throw new InvalidDataException(
                        "Third-party download did not match pinned SHA-256. Installation aborted.");
                File.Move(tempPath, destination, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (IOException) { }
            }
        }
        progress?.Report(1);
        var manifest = NudeNetPreset.Create();
        manifest.Validate();
        string stagedManifest = ManifestPath + ".partial";
        try
        {
            await File.WriteAllTextAsync(stagedManifest,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(stagedManifest, ManifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagedManifest)) File.Delete(stagedManifest);
        }
        // Final model path and hash revalidated before inference.
        ModelManifest.LoadAndValidate(ManifestPath);
        return ManifestPath;
    }

    public static async Task<bool> CheckExistingAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
