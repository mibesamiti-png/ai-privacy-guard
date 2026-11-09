using PrivacyGuard.Core;
using Xunit;

public sealed class VideoReviewBindingTests
{
    private static readonly string A = new('A', 64);
    private static readonly string B = new('B', 64);

    [Fact]
    public void SchemaTwo_RequiresExactContentHash()
    {
        Assert.True(VideoReviewBinding.CanTrustApprovals(2, A, A.ToLowerInvariant()));
        Assert.False(VideoReviewBinding.CanTrustApprovals(2, A, B));
    }

    [Fact]
    public void LegacySchema_NeverRetainsApprovals()
    {
        Assert.True(VideoReviewBinding.IsLegacy(1));
        Assert.False(VideoReviewBinding.CanTrustApprovals(1, A, A));
        Assert.False(VideoReviewBinding.CanTrustApprovals(3, A, A));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void InvalidHash_IsRejected(string value)
    {
        Assert.False(VideoReviewBinding.IsWellFormedSha256(value));
        Assert.False(VideoReviewBinding.CanTrustApprovals(2, value, A));
    }

    [Fact]
    public void ReceiptVisualAudit_DoesNotChangeCryptographicReuse()
    {
        var job = new BatchJob("C:/media/photo.jpg", BatchMediaKind.Image, 90, 1234);
        var receipt = new BatchExportReceipt(job.SourcePath, job.Kind, job.SizeBytes,
            job.ModifiedUtcTicks, A, "REVIEW", "C:/output/photo.png",
            BatchExportStatus.Completed, 100, B);
        var checkedReceipt = receipt with
        {
            PreviewCheckedAtUtc = DateTimeOffset.UtcNow,
            PreviewCheckedAtUs = 5_000_000
        };
        Assert.True(BatchExportPlanner.Reusable(checkedReceipt, job, A,
            "REVIEW", checkedReceipt.OutputPath, 100, B));
        Assert.False(BatchExportPlanner.Reusable(checkedReceipt, job, A,
            "CHANGED", checkedReceipt.OutputPath, 100, B));
    }
}
