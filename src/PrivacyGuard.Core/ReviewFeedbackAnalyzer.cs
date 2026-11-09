using System.Text;

namespace PrivacyGuard.Core;

/// <summary>
/// Summarizes decisions on model suggestions. This is feedback, NOT detection
/// precision/recall: rejected candidates are not exhaustive negative examples,
/// and undetected exposure is absent from the queue.
/// </summary>
public sealed record ReviewFeedbackRow(
    string ModelId, string Category, int Total, int Approved, int Rejected,
    int Deferred, int Pending, double? AverageConfidence);

public static class ReviewFeedbackAnalyzer
{
    public static IReadOnlyList<ReviewFeedbackRow> Summarize(IEnumerable<DetectionRecord> records)
    {
        return records.Where(r => r.Origin.StartsWith("onnx:", StringComparison.Ordinal))
            .GroupBy(r => (Model: r.Origin[5..], r.Category))
            .OrderBy(g => g.Key.Model, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Category, StringComparer.Ordinal)
            .Select(g => new ReviewFeedbackRow(g.Key.Model, g.Key.Category,
                g.Count(), g.Count(r => r.Decision == ReviewDecision.Approved),
                g.Count(r => r.Decision == ReviewDecision.Rejected),
                g.Count(r => r.Decision == ReviewDecision.Deferred),
                g.Count(r => r.Decision == ReviewDecision.Pending),
                g.Any(r => r.Confidence is not null)
                    ? g.Where(r => r.Confidence is not null).Average(r => r.Confidence!.Value)
                    : null))
            .ToArray();
    }

    public static string ToCsv(IEnumerable<ReviewFeedbackRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("model_id,category,total,approved,rejected,deferred,pending,average_model_score");
        foreach (var r in rows)
        {
            sb.Append(Escape(r.ModelId)).Append(',').Append(Escape(r.Category)).Append(',')
                .Append(r.Total).Append(',').Append(r.Approved).Append(',')
                .Append(r.Rejected).Append(',').Append(r.Deferred).Append(',')
                .Append(r.Pending).Append(',')
                .Append(r.AverageConfidence?.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine();
        }
        return sb.ToString();
    }

    private static string Escape(string s) =>
        "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
