using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

// Schema 1 video reviews were bound only to path/size/mtime. When loaded, ALL decisions
// are reset to Pending and require explicit re-approval; no implicit trust migration.
public sealed record VideoReviewProject(int SchemaVersion, string SourcePath, long FileSize,
    long LastWriteTicks, List<VideoMaskCandidate> Candidates, string? SourceSha256 = null);

/// <summary>Private local metadata. Source hash is validated on load and during batch export.</summary>
public static class VideoReviewStore
{
    private sealed record HashCacheEntry(long Size, long ModifiedUtcTicks, string Sha256);
    private static readonly ConcurrentDictionary<string, HashCacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static string GetPath(string path)
    {
        var info = new FileInfo(path);
        string key = Path.GetFullPath(path).ToUpperInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        string hashed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIPrivacyGuard", "video_reviews", hashed + ".json");
    }

    private static string ComputeHash(string source)
    {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Remember(string source, FileInfo info, string hash) =>
        Cache[Path.GetFullPath(source)] = new HashCacheEntry(info.Length, info.LastWriteTimeUtc.Ticks, hash);

    public static List<VideoMaskCandidate> Load(string source, string? verifiedSourceSha256 = null)
    {
        var info = new FileInfo(source);
        string path = GetPath(source);
        // A batch export supplies its independently calculated current SHA-256. For ordinary
        // review opening always hash the actual file, never trust only the cached metadata.
        string actualHash = verifiedSourceSha256 ?? ComputeHash(source);
        if (!VideoReviewBinding.IsWellFormedSha256(actualHash))
            throw new InvalidDataException("動画ファイルの検証ハッシュが不正です。");
        Remember(source, info, actualHash);
        if (!File.Exists(path)) return new();
        var session = JsonSerializer.Deserialize<VideoReviewProject>(File.ReadAllText(path));
        if (session is null || session.SchemaVersion is not (1 or 2) ||
            !string.Equals(session.SourcePath, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase) ||
            session.FileSize != info.Length || session.LastWriteTicks != info.LastWriteTimeUtc.Ticks ||
            session.Candidates is null || session.Candidates.Count > 30_000)
            throw new InvalidDataException("動画の確認記録が現在のファイルに対応していません。");
        if (VideoReviewBinding.IsLegacy(session.SchemaVersion))
        {
            // A legacy review is preserved as a suggestion only. All prior approvals are revoked.
            return session.Candidates.Select(x => x with { Decision = ReviewDecision.Pending }).ToList();
        }
        if (!VideoReviewBinding.CanTrustApprovals(session.SchemaVersion, session.SourceSha256, actualHash))
            throw new InvalidDataException("動画内容が以前の確認時から変わっています。再確認してください。");
        return session.Candidates;
    }

    public static void Save(string source, IReadOnlyList<VideoMaskCandidate> candidates)
    {
        var info = new FileInfo(source);
        string full = Path.GetFullPath(source);
        // Reuse the hash measured on opening. Batch export re-hashes the original and validates
        // it against the saved version before publication; size/mtime alone never authorizes it.
        string hash = Cache.TryGetValue(full, out var cached) &&
            cached.Size == info.Length && cached.ModifiedUtcTicks == info.LastWriteTimeUtc.Ticks
                ? cached.Sha256 : ComputeHash(source);
        if (!VideoReviewBinding.IsWellFormedSha256(hash))
            throw new InvalidDataException("動画ハッシュを計算できませんでした。");
        Remember(source, info, hash);
        string output = GetPath(source);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var session = new VideoReviewProject(VideoReviewBinding.CurrentSchemaVersion, full, info.Length,
            info.LastWriteTimeUtc.Ticks, candidates.ToList(), hash);
        string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(session,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, output, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
