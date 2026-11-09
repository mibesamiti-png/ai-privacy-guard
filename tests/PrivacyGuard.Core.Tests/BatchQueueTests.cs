using PrivacyGuard.Core;
using Xunit;

public sealed class BatchQueueTests
{
    [Fact]
    public void Scan_RecognizesSupportedMedia_ExcludesGeneratedFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "apg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Directory.CreateDirectory(Path.Combine(root, "AIPrivacyGuard_Output"));
            File.WriteAllBytes(Path.Combine(root, "photo.PNG"), [1]);
            File.WriteAllBytes(Path.Combine(root, "nested", "movie.mkv"), [2]);
            File.WriteAllBytes(Path.Combine(root, "notes.txt"), [3]);
            File.WriteAllBytes(Path.Combine(root, "AIPrivacyGuard_Output", "copy.png"), [4]);
            var flat = BatchQueuePlanner.Scan(root, false);
            var recursive = BatchQueuePlanner.Scan(root, true);
            Assert.Single(flat);
            Assert.Equal(2, recursive.Count);
            Assert.Contains(recursive, x => x.Kind == BatchMediaKind.Video);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Reconcile_RestoresCompletedAndResetsInterruptedOrChanged()
    {
        var a = new BatchJob(@"C:\media\a.png", BatchMediaKind.Image, 12, 10);
        var b = new BatchJob(@"C:\media\b.mp4", BatchMediaKind.Video, 34, 20);
        var c = new BatchJob(@"C:\media\c.png", BatchMediaKind.Image, 56, 30);
        var saved = new [] { a with { Status=BatchJobStatus.Completed, CandidateCount=3 },
            b with { Status=BatchJobStatus.Running },
            c with { Status=BatchJobStatus.Completed, CandidateCount=1 } };
        var actual = BatchQueuePlanner.Reconcile([a, b, c with { SizeBytes=57 }], saved);
        Assert.Equal(BatchJobStatus.Completed, actual[0].Status);
        Assert.Equal(3, actual[0].CandidateCount);
        Assert.Equal(BatchJobStatus.Pending, actual[1].Status);
        Assert.Equal(BatchJobStatus.Pending, actual[2].Status);
    }

    [Fact]
    public void IsUnder_RejectsSiblingWithMatchingPrefix()
    {
        Assert.False(BatchQueuePlanner.IsUnder(Path.Combine("/tmp", "media2", "a.png"),
            Path.Combine("/tmp", "media")));
    }

    [Fact]
    public void SignatureMatches_DetectsChangedLength()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, [1,2,3]);
            var info = new FileInfo(file);
            var job = new BatchJob(file, BatchMediaKind.Image, info.Length, info.LastWriteTimeUtc.Ticks);
            Assert.True(BatchQueuePlanner.SignatureMatches(job));
            File.AppendAllText(file, "changed");
            Assert.False(BatchQueuePlanner.SignatureMatches(job));
        }
        finally { File.Delete(file); }
    }
}
