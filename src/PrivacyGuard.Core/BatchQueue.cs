using System.IO;
using System.Text.Json.Serialization;

namespace PrivacyGuard.Core;

public enum BatchMediaKind { Image, Video }
public enum BatchJobStatus { Pending, Running, Completed, Failed }

/// <summary>Only filenames, status and counts are persisted. Never raw images or thumbnails.</summary>
public sealed record BatchJob(string SourcePath, BatchMediaKind Kind, long SizeBytes,
    long ModifiedUtcTicks, BatchJobStatus Status = BatchJobStatus.Pending,
    int CandidateCount = 0, string? Error = null, string? ModelId = null)
{
    [JsonIgnore]
    public string Display => $"{Path.GetFileName(SourcePath)}   [{(Kind == BatchMediaKind.Image ? "画像" : "動画")}] " +
        $"{Status switch { BatchJobStatus.Pending => "待機", BatchJobStatus.Running => "解析中", BatchJobStatus.Completed => "解析済", _ => "エラー" }}" +
        (Status == BatchJobStatus.Completed ? $" / 新規候補{CandidateCount}件（安全判定ではありません）" : "") +
        (string.IsNullOrWhiteSpace(Error) ? "" : $" / {Error}");
}

public sealed record BatchQueueSnapshot(int SchemaVersion, string Root, bool Recursive,
    List<BatchJob> Jobs);

/// <summary>Deterministic inventory and resumption policy. Never opens, deletes or modifies media.</summary>
public static class BatchQueuePlanner
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v" };

    public const int MaxFiles = 10_000;

    public static bool IsUnder(string path, string root)
    {
        string candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<BatchJob> Scan(string root, bool recursive,
        CancellationToken cancellationToken = default)
    {
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        string excludedOutput = Path.Combine(fullRoot, "AIPrivacyGuard_Output");
        var jobs = new List<BatchJob>();
        foreach (string file in Directory.EnumerateFiles(fullRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnder(file, excludedOutput)) continue;
            string ext = Path.GetExtension(file);
            BatchMediaKind? kind = Images.Contains(ext) ? BatchMediaKind.Image :
                Videos.Contains(ext) ? BatchMediaKind.Video : null;
            if (kind is null) continue;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length == 0 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                jobs.Add(new BatchJob(info.FullName, kind.Value, info.Length, info.LastWriteTimeUtc.Ticks));
            }
            catch (IOException) { /* Files being copied can be skipped until the next scan. */ }
            catch (UnauthorizedAccessException) { }
            if (jobs.Count > MaxFiles)
                throw new NotSupportedException($"一度のスキャンは最大{MaxFiles:N0}ファイルです。");
        }
        return jobs.OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<BatchJob> Reconcile(IEnumerable<BatchJob> current,
        IEnumerable<BatchJob>? saved)
    {
        ArgumentNullException.ThrowIfNull(current);
        var indexed = (saved ?? Array.Empty<BatchJob>())
            .GroupBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return current.Select(job =>
        {
            if (!indexed.TryGetValue(job.SourcePath, out var old) ||
                old.Kind != job.Kind || old.SizeBytes != job.SizeBytes ||
                old.ModifiedUtcTicks != job.ModifiedUtcTicks) return job;
            // A process interrupted while Running must not claim to be completed.
            return old.Status == BatchJobStatus.Running
                ? job with { ModelId = old.ModelId }
                : job with { Status = old.Status, CandidateCount = old.CandidateCount,
                    Error = old.Error, ModelId = old.ModelId };
        }).ToArray();
    }

    public static bool SignatureMatches(BatchJob job)
    {
        try
        {
            var info = new FileInfo(job.SourcePath);
            return info.Exists && info.Length == job.SizeBytes &&
                info.LastWriteTimeUtc.Ticks == job.ModifiedUtcTicks;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
