using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PrivacyGuard.Core;

public enum BatchExportStatus { Pending, Running, Completed, NeedsReview, Failed }

/// <summary>Receipts are metadata only. Never serialize images or frame content.</summary>
public sealed record BatchExportReceipt(
    string SourcePath, BatchMediaKind Kind, long SourceSize, long SourceModifiedTicks,
    string SourceSha256, string ReviewSha256, string OutputPath,
    BatchExportStatus Status, long OutputSize = 0, string? OutputSha256 = null,
    string? Error = null, DateTimeOffset? PreviewCheckedAtUtc = null, long? PreviewCheckedAtUs = null)
{
    [JsonIgnore]
    public string Display => Path.GetFileName(SourcePath) + " → " + (Status switch
    {
        BatchExportStatus.Pending => "待機", BatchExportStatus.Running => "書出中",
        BatchExportStatus.Completed => PreviewCheckedAtUtc is null ? "出力済み・比較未記録" : "出力済み・比較記録あり", BatchExportStatus.NeedsReview => "確認待ち", _ => "エラー"
    }) + (string.IsNullOrEmpty(Error) ? "" : " / " + Error);
}

public sealed record BatchExportSnapshot(int SchemaVersion, string Root,
    string OutputRoot, List<BatchExportReceipt> Receipts);

public static class BatchExportPlanner
{
    public const string DefaultOutputName = "AIPrivacyGuard_Output";

    public static string DefaultOutputRoot(string sourceRoot) =>
        Path.Combine(Path.GetFullPath(sourceRoot), DefaultOutputName);

    public static void ValidateOutputRoot(string sourceRoot, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Input and output directories are required.");
        string source = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string output = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string allowedNested = DefaultOutputRoot(source);
        // Do not place outputs inside an arbitrary subtree that the next folder scan could ingest.
        if (BatchQueuePlanner.IsUnder(output, source) &&
            !BatchQueuePlanner.IsUnder(output, allowedNested))
            throw new ArgumentException("出力先は対象フォルダの外か AIPrivacyGuard_Output 内を指定してください。");
        if (string.Equals(output, source, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("出力先は入力フォルダにできません。");
    }

    public static string DestinationFor(BatchJob job, string sourceRoot, string outputRoot)
    {
        ValidateOutputRoot(sourceRoot, outputRoot);
        if (!BatchQueuePlanner.IsUnder(job.SourcePath, sourceRoot))
            throw new ArgumentException("対象フォルダ外のファイルは書き出しません。");
        string relative = Path.GetRelativePath(Path.GetFullPath(sourceRoot), Path.GetFullPath(job.SourcePath));
        if (Path.IsPathRooted(relative) || relative is ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("不正な相対パスです。");
        string directory = Path.GetDirectoryName(relative) ?? "";
        string extension = Path.GetExtension(relative).TrimStart('.').ToLowerInvariant();
        string outputExtension = job.Kind == BatchMediaKind.Image ? ".png" : ".mp4";
        // An input name such as scene.jpg and scene.png must not collide after conversion.
        string baseName = Path.GetFileNameWithoutExtension(relative) + "__" + extension + "_guarded";
        string path = Path.GetFullPath(Path.Combine(outputRoot, directory, baseName + outputExtension));
        if (!BatchQueuePlanner.IsUnder(path, outputRoot) ||
            string.Equals(path, Path.GetFullPath(job.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("書き出し先が安全ではありません。");
        return path;
    }

    public static string Fingerprint(string serializedReviewWithSettings) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serializedReviewWithSettings)));

    public static bool Reusable(BatchExportReceipt? receipt, BatchJob source, string sourceHash,
        string reviewHash, string plannedTarget, long outputSize, string outputHash) =>
        receipt is { Status: BatchExportStatus.Completed } &&
        string.Equals(receipt.SourcePath, source.SourcePath, StringComparison.OrdinalIgnoreCase) &&
        receipt.Kind == source.Kind && receipt.SourceSize == source.SizeBytes &&
        receipt.SourceModifiedTicks == source.ModifiedUtcTicks &&
        string.Equals(receipt.SourceSha256, sourceHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(receipt.ReviewSha256, reviewHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(receipt.OutputPath, plannedTarget, StringComparison.OrdinalIgnoreCase) &&
        receipt.OutputSize == outputSize &&
        string.Equals(receipt.OutputSha256, outputHash, StringComparison.OrdinalIgnoreCase);
}
