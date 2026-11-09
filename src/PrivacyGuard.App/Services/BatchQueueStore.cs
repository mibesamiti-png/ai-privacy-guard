using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

/// <summary>Local-only atomic queue snapshots. Personal media paths are sensitive metadata.</summary>
public static class BatchQueueStore
{
    private static string GetPath(string root)
    {
        string normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIPrivacyGuard", "batch_queues", id + ".json");
    }

    public static BatchQueueSnapshot? Load(string root)
    {
        string file = GetPath(root);
        if (!File.Exists(file)) return null;
        var snapshot = JsonSerializer.Deserialize<BatchQueueSnapshot>(File.ReadAllText(file));
        if (snapshot is null || snapshot.SchemaVersion != 1 ||
            !string.Equals(Path.GetFullPath(snapshot.Root), Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase) || snapshot.Jobs is null ||
            snapshot.Jobs.Count > BatchQueuePlanner.MaxFiles)
            throw new InvalidDataException("一括処理の保存データが不正です。");
        foreach (var job in snapshot.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.SourcePath) ||
                !BatchQueuePlanner.IsUnder(job.SourcePath, root) ||
                job.SizeBytes < 0 || job.CandidateCount < 0 ||
                !Enum.IsDefined(job.Kind) || !Enum.IsDefined(job.Status))
                throw new InvalidDataException("一括処理ジョブに不正な情報が含まれます。");
        }
        return snapshot;
    }

    public static void Save(string root, bool recursive, IReadOnlyCollection<BatchJob> jobs)
    {
        if (jobs.Count > BatchQueuePlanner.MaxFiles) throw new ArgumentOutOfRangeException(nameof(jobs));
        var payload = new BatchQueueSnapshot(1, Path.GetFullPath(root), recursive, jobs.ToList());
        string target = GetPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
