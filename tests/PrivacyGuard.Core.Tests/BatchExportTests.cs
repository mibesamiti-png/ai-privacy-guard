using PrivacyGuard.Core;
using Xunit;

public sealed class BatchExportTests
{
    [Fact]
    public void Output_UsesSeparateDirectory_PreservesTreeAndOriginalExtensions()
    {
        string root = Path.Combine(Path.GetTempPath(), "apg-batch-" + Guid.NewGuid().ToString("N"));
        string output = BatchExportPlanner.DefaultOutputRoot(root);
        var a = new BatchJob(Path.Combine(root, "nested", "scene.jpg"), BatchMediaKind.Image, 12, 2);
        var b = new BatchJob(Path.Combine(root, "nested", "scene.png"), BatchMediaKind.Image, 20, 2);
        var c = new BatchJob(Path.Combine(root, "clip.mkv"), BatchMediaKind.Video, 30, 2);
        Assert.Equal(Path.Combine(output, "nested", "scene__jpg_guarded.png"),
            BatchExportPlanner.DestinationFor(a, root, output));
        Assert.NotEqual(BatchExportPlanner.DestinationFor(a, root, output),
            BatchExportPlanner.DestinationFor(b, root, output));
        Assert.EndsWith("clip__mkv_guarded.mp4", BatchExportPlanner.DestinationFor(c, root, output));
    }

    [Fact]
    public void Output_RejectsInputTreeAndUnrelatedPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "apg-in-" + Guid.NewGuid().ToString("N"));
        var input = new BatchJob(Path.Combine(root, "file.png"), BatchMediaKind.Image, 1, 1);
        Assert.Throws<ArgumentException>(() => BatchExportPlanner.ValidateOutputRoot(root, root));
        Assert.Throws<ArgumentException>(() => BatchExportPlanner.ValidateOutputRoot(root, Path.Combine(root, "unsafe")));
        Assert.Throws<ArgumentException>(() => BatchExportPlanner.DestinationFor(
            new BatchJob(Path.Combine(Path.GetTempPath(), "outside.png"), BatchMediaKind.Image, 1, 1),
            root, BatchExportPlanner.DefaultOutputRoot(root)));
        Assert.True(BatchQueuePlanner.IsUnder(BatchExportPlanner.DestinationFor(input,
            root, BatchExportPlanner.DefaultOutputRoot(root)), BatchExportPlanner.DefaultOutputRoot(root)));
    }

    [Fact]
    public void ReceiptReuse_RequiresMatchingSourceReviewOutputAndHash()
    {
        string root = Path.Combine(Path.GetTempPath(), "apg-receipt-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "photo.jpg");
        string output = Path.Combine(root, "AIPrivacyGuard_Output", "photo__jpg_guarded.png");
        var job = new BatchJob(source, BatchMediaKind.Image, 100, 123);
        var receipt = new BatchExportReceipt(source, job.Kind, job.SizeBytes,
            job.ModifiedUtcTicks, "SOURCE", "REVIEW", output, BatchExportStatus.Completed, 88, "OUTPUT");
        Assert.True(BatchExportPlanner.Reusable(receipt, job, "SOURCE", "REVIEW", output, 88, "OUTPUT"));
        Assert.False(BatchExportPlanner.Reusable(receipt, job, "SOURCE", "REVIEW-EDIT", output, 88, "OUTPUT"));
        Assert.False(BatchExportPlanner.Reusable(receipt, job, "SOURCE-EDIT", "REVIEW", output, 88, "OUTPUT"));
        Assert.False(BatchExportPlanner.Reusable(receipt, job, "SOURCE", "REVIEW", output, 88, "TAMPERED"));
        Assert.False(BatchExportPlanner.Reusable(receipt with { Status = BatchExportStatus.Running },
            job, "SOURCE", "REVIEW", output, 88, "OUTPUT"));
    }

    [Fact]
    public void Fingerprint_IsDeterministicAndChangesOnDecisionChange()
    {
        var a = BatchExportPlanner.Fingerprint("[{\"decision\":\"Approved\"}]");
        var b = BatchExportPlanner.Fingerprint("[{\"decision\":\"Approved\"}]");
        var c = BatchExportPlanner.Fingerprint("[{\"decision\":\"Rejected\"}]");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}

public sealed class ApprovedOnlyMaskTests
{
    [Fact]
    public void MaskProcessor_ChangesApprovedRegionOnly()
    {
        var sourceId = Guid.NewGuid();
        var sourcePixels = Enumerable.Repeat((byte)240, 12 * 12 * 4).ToArray();
        var source = new PixelBuffer(12, 12, sourcePixels);
        var approved = DetectionRecord.Manual(sourceId, new NormalizedRect(0, 0, 0.25, 0.25))
            with { Decision = ReviewDecision.Approved };
        var pending = DetectionRecord.Manual(sourceId, new NormalizedRect(0.5, 0.5, 0.25, 0.25));
        var rejected = DetectionRecord.Manual(sourceId, new NormalizedRect(0.75, 0.75, 0.2, 0.2))
            with { Decision = ReviewDecision.Rejected };
        var result = MaskProcessor.Apply(source, [approved, pending, rejected], MaskStyle.Black, 16, 0);
        int indexApproved = (1 * 12 + 1) * 4;
        int indexPending = (7 * 12 + 7) * 4;
        int indexRejected = (10 * 12 + 10) * 4;
        Assert.Equal((byte)0, result.Bgra[indexApproved]);
        Assert.Equal((byte)240, result.Bgra[indexPending]);
        Assert.Equal((byte)240, result.Bgra[indexRejected]);
        Assert.Equal((byte)240, source.Bgra[indexApproved]); // Original remains intact.
    }
}
