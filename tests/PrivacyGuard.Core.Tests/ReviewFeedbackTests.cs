using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class ReviewFeedbackTests
{
    private static DetectionRecord Make(string origin, string category, ReviewDecision decision,
        double? confidence = .8) => new(Guid.NewGuid(), Guid.NewGuid(), category, confidence,
        new NormalizedRect(.1, .1, .2, .2), decision, origin, DateTimeOffset.UtcNow);

    [Fact]
    public void FeedbackIgnoresManualAndMockAndSeparatesModels()
    {
        var records = new[]
        {
            Make("mock", "TEST", ReviewDecision.Approved),
            Make("manual", "TEST", ReviewDecision.Approved),
            Make("onnx:detector-A", "TEST", ReviewDecision.Approved, .9),
            Make("onnx:detector-A", "TEST", ReviewDecision.Rejected, .7),
            Make("onnx:detector-A", "TEST", ReviewDecision.Pending, .6),
            Make("onnx:detector-B", "TEST", ReviewDecision.Deferred, .95)
        };
        var rows = ReviewFeedbackAnalyzer.Summarize(records);
        Assert.Equal(2, rows.Count);
        Assert.Equal("detector-A", rows[0].ModelId);
        Assert.Equal(3, rows[0].Total);
        Assert.Equal(1, rows[0].Approved);
        Assert.Equal(1, rows[0].Rejected);
        Assert.Equal(1, rows[0].Pending);
        Assert.Equal(.73333333, rows[0].AverageConfidence!.Value, 6);
        Assert.Equal(1, rows[1].Deferred);
    }

    [Fact]
    public void CsvQuotesPotentiallyUntrustedCategories()
    {
        var row = new ReviewFeedbackRow("model,\"A", "test,category", 1, 1, 0, 0, 0, .7);
        var csv = ReviewFeedbackAnalyzer.ToCsv(new[] { row });
        Assert.Contains("\"model,\"\"A\"", csv);
        Assert.Contains("\"test,category\"", csv);
        Assert.Contains("0.7000", csv);
        Assert.DoesNotContain("file_path", csv);
    }
}
