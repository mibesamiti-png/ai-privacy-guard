using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PrivacyGuard.AI;
using PrivacyGuard.Core;

/// <summary>
/// Adult apparel domain shift and model threshold diagnostics. A provisional
/// 'lingerie visible' label is NOT a human-verified privacy incident positive.
/// </summary>
internal static class ThresholdSweep
{
    public static async Task<int> RunAsync(string modelFile, string fixtureDir, string reportDir)
    {
        if (!File.Exists(modelFile) || !Directory.Exists(fixtureDir))
            throw new FileNotFoundException("Missing pinned model or test source directory.");
        string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(modelFile)));
        if (!hash.Equals(NudeNetPreset.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model SHA-256 mismatch");

        Directory.CreateDirectory(reportDir);
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtureDir,"fixtures.json")));
        JsonElement[] cases = input.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        float[] thresholds = [0.10f, 0.25f, 0.40f];
        var runs = new List<object>();
        var csv = new List<string>
        {
            "threshold,id,group,reference_label,category,count,max_confidence,source_url"
        };
        foreach (float threshold in thresholds)
        {
            var manifest = NudeNetPreset.Create();
            manifest.ScoreThreshold = threshold;
            using var detector = new OnnxExposureDetector(manifest, modelFile);
            var rows = new List<object>();
            int total = 0, imagesWithHits = 0, adultUnderwearSamples = 0, adultUnderwearHits = 0;
            foreach (var sample in cases)
            {
                string id = sample.GetProperty("id").GetString()!;
                string group = sample.TryGetProperty("group", out var g) ? g.GetString()! : "";
                string label = sample.TryGetProperty("visual_reference", out var l)
                    ? l.GetString()! : "not_labeled";
                string sourceUrl = sample.TryGetProperty("source_url", out var source)
                    ? source.GetString()! : "";
                string rawPath = Path.Combine(fixtureDir,
                    sample.GetProperty("bgra").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                int w = sample.GetProperty("width").GetInt32();
                int h = sample.GetProperty("height").GetInt32();
                var frame = new PixelBuffer(w, h, await File.ReadAllBytesAsync(rawPath), copy:false);
                IReadOnlyList<DetectionCandidate> predictions =
                    await detector.DetectAsync(frame, CancellationToken.None);
                if (predictions.Count > 200 ||
                    predictions.Any(x => !double.IsFinite(x.Score) ||
                        x.Bounds.X < 0 || x.Bounds.Y < 0 ||
                        x.Bounds.X+x.Bounds.Width > 1.000001 ||
                        x.Bounds.Y+x.Bounds.Height > 1.000001))
                    throw new InvalidDataException("Malformed detection on " + id);
                int count = predictions.Count;
                total += count;
                if(count>0) imagesWithHits++;
                if(label == "lingerie_visible")
                {
                    adultUnderwearSamples++;
                    if(count>0) adultUnderwearHits++;
                }
                var boxes = predictions.Select(x=>new
                {
                    x.Category,
                    confidence = Math.Round(x.Score,5),
                    xywh = new[]{x.Bounds.X,x.Bounds.Y,x.Bounds.Width,x.Bounds.Height}
                }).ToArray();
                rows.Add(new{ id,group,reference_label=label,source_url=sourceUrl,
                    detection_count=count, detections=boxes});
                if(count == 0)
                    csv.Add(Csv(threshold,id,group,label,"",0,0,sourceUrl));
                else
                    foreach(var category in predictions.GroupBy(d=>d.Category))
                        csv.Add(Csv(threshold,id,group,label,category.Key,
                            category.Count(),category.Max(x=>x.Score),sourceUrl));
                Console.WriteLine($"SWEEP threshold={threshold:0.00} {id} candidates={count}");
            }
            runs.Add(new{threshold,images=cases.Length,images_with_any_detection=imagesWithHits,
                total_candidates=total,visible_lingerie_reference_count=adultUnderwearSamples,
                visible_lingerie_with_any_detection=adultUnderwearHits,results=rows});
        }
        var payload=new
        {
            report_type="nudenet_320n_adult_apparel_threshold_diagnostic",
            generated_utc=DateTimeOffset.UtcNow.ToString("o"),
            model_sha256=NudeNetPreset.ModelSha256,
            model_input="320x320 RGB square-padded",
            interpretation="A garment-visible reference is NOT a true privacy incident label. No claim about panty-shot recall or precision is possible.",
            thresholds,
            total_input_images=cases.Length,
            threshold_runs=runs
        };
        await File.WriteAllTextAsync(Path.Combine(reportDir,"threshold_sweep.json"),
            JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllLinesAsync(Path.Combine(reportDir,"threshold_sweep.csv"),csv);
        Console.WriteLine($"NUDENET_THRESHOLD_SWEEP_PASS: {cases.Length} images x {thresholds.Length} thresholds");
        return 0;
    }

    private static string Csv(float threshold,string id,string group,string label,
        string category,int count,double score,string url)
    {
        static string Quote(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
        return string.Join(",",[
            threshold.ToString("0.00",CultureInfo.InvariantCulture),
            Quote(id),Quote(group),Quote(label),Quote(category),
            count.ToString(CultureInfo.InvariantCulture),
            score.ToString("0.00000",CultureInfo.InvariantCulture),Quote(url)
        ]);
    }
}
