using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

/// <summary>Separate atomic receipts for export operations; no input/output media bytes are persisted.</summary>
public static class BatchExportStore
{
    private static string StorePath(string sourceRoot, string outputRoot)
    {
        string key = Path.GetFullPath(sourceRoot).ToUpperInvariant() + "|" +
            Path.GetFullPath(outputRoot).ToUpperInvariant();
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIPrivacyGuard", "batch_exports", id + ".json");
    }

    public static List<BatchExportReceipt> Load(string sourceRoot, string outputRoot)
    {
        BatchExportPlanner.ValidateOutputRoot(sourceRoot, outputRoot);
        string path = StorePath(sourceRoot, outputRoot);
        if (!File.Exists(path)) return new();
        var snapshot = JsonSerializer.Deserialize<BatchExportSnapshot>(File.ReadAllText(path));
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Receipts is null ||
            snapshot.Receipts.Count > BatchQueuePlanner.MaxFiles ||
            !string.Equals(Path.GetFullPath(snapshot.Root), Path.GetFullPath(sourceRoot), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(snapshot.OutputRoot), Path.GetFullPath(outputRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("保存された一括出力記録が不正です。");
        foreach (var item in snapshot.Receipts)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                string.IsNullOrWhiteSpace(item.OutputPath) ||
                !BatchQueuePlanner.IsUnder(item.SourcePath, sourceRoot) ||
                !BatchQueuePlanner.IsUnder(item.OutputPath, outputRoot) ||
                !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Status) ||
                item.SourceSize < 0 || item.OutputSize < 0 ||
                string.Equals(Path.GetFullPath(item.OutputPath), Path.GetFullPath(item.SourcePath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("一括出力記録に不正なパスまたは状態があります。");
        }
        return snapshot.Receipts.Select(x => x.Status == BatchExportStatus.Running
                ? x with { Status = BatchExportStatus.Failed, Error = "前回の出力が中断されました。" } : x)
            .ToList();
    }

    public static void Save(string sourceRoot, string outputRoot, IReadOnlyList<BatchExportReceipt> receipts)
    {
        BatchExportPlanner.ValidateOutputRoot(sourceRoot, outputRoot);
        if (receipts.Count > BatchQueuePlanner.MaxFiles) throw new ArgumentOutOfRangeException(nameof(receipts));
        string path = StorePath(sourceRoot, outputRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var snapshot = new BatchExportSnapshot(1, Path.GetFullPath(sourceRoot),
                Path.GetFullPath(outputRoot), receipts.ToList());
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
