using System.Diagnostics;
using System.Text.Json;
using PrivacyGuard.AI;
using PrivacyGuard.Core;

internal static class VisualEval
{
    public static async Task<int> RunAsync(string modelPath, string fixtureFolder, string reportFolder)
    {
        if (!File.Exists(modelPath) || !Directory.Exists(fixtureFolder))
        {
            Console.Error.WriteLine("Missing model or synthetic fixture directory.");
            return 2;
        }
        var manifest = NudeNetPreset.Create();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(modelPath)));
        if (!hash.Equals(manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pinned model SHA mismatch. Aborting visual evaluation.");

        Directory.CreateDirectory(reportFolder);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixtureFolder, "fixtures.json")));
        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        using var detector = new OnnxExposureDetector(manifest, modelPath);
        var all = new List<object>();
        int total = 0;
        foreach (var sample in cases)
        {
            string id = sample.GetProperty("id").GetString()!;
            string description = sample.GetProperty("description").GetString()!;
            int w = sample.GetProperty("width").GetInt32();
            int h = sample.GetProperty("height").GetInt32();
            string pixelsPath = Path.Combine(fixtureFolder,
                sample.GetProperty("bgra").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            byte[] bgra = await File.ReadAllBytesAsync(pixelsPath);
            var frame = new PixelBuffer(w, h, bgra, copy: false);
            var sw = Stopwatch.StartNew();
            var candidates = await detector.DetectAsync(frame, CancellationToken.None);
            sw.Stop();
            total += candidates.Count;

            var approvedForSimulation = candidates.Select(c => DetectionRecord.Manual(Guid.Empty,
                c.Bounds) with { Category = c.Category, Decision = ReviewDecision.Approved }).ToArray();
            var masked = MaskProcessor.Apply(frame, approvedForSimulation, MaskStyle.Black,
                padding: 0.25);
            var annotated = DrawRectangles(frame, candidates);
            string annotatedName = id + "_annotated.bmp";
            string maskedName = id + "_blackout_preview.bmp";
            await File.WriteAllBytesAsync(Path.Combine(reportFolder, annotatedName),
                BmpFrameEncoder.Encode(annotated));
            await File.WriteAllBytesAsync(Path.Combine(reportFolder, maskedName),
                BmpFrameEncoder.Encode(masked));

            var objects = candidates.Select(c => new
            {
                category = c.Category,
                confidence = Math.Round(c.Score, 6),
                normalized_xywh = new[] { c.Bounds.X, c.Bounds.Y, c.Bounds.Width, c.Bounds.Height },
                pixel_xywh = ToBox(c.Bounds, w, h)
            }).ToArray();
            string sourceUrl = sample.TryGetProperty("source_url", out var sourceEl)
                ? (sourceEl.GetString() ?? "") : "";
            string group = sample.TryGetProperty("group", out var groupEl)
                ? (groupEl.GetString() ?? "original_synthetic") : "original_synthetic";
            string license = sample.TryGetProperty("source", out var licenseEl)
                ? (licenseEl.GetString() ?? "") : "";
            bool permittedToRedistribute = !sample.TryGetProperty("allowed_to_redistribute",
                out var allowEl) || allowEl.GetBoolean();
            all.Add(new { id, description, group, source_url = sourceUrl, license,
                source_published_in_artifact = permittedToRedistribute, width = w, height = h,
                count = objects.Length, elapsed_ms = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                detections = objects, annotated_bmp = permittedToRedistribute ? annotatedName : null,
                mask_preview_bmp = permittedToRedistribute ? maskedName : null });
            Console.WriteLine($"CASE {id}: {objects.Length} candidate(s), {sw.Elapsed.TotalMilliseconds:0.0}ms");
        }
        var report = new
        {
            report_type = "nudenet_320n_real_onnx_visual_eval",
            generated_utc = DateTimeOffset.UtcNow.ToString("o"),
            model_id = manifest.ModelId,
            model_sha256 = manifest.ModelSha256,
            threshold = manifest.ScoreThreshold,
            nms_iou = manifest.NmsIouThreshold,
            disclaimer = "Published public media and synthetic controls do not provide consented positive under-skirt exposure labels. This is a domain-shift exploration, not a false-negative benchmark.",
            image_count = cases.Length,
            total_candidates = total,
            results = all
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(reportFolder, "results.json"), json);
        var markdown = new List<string>
        {
            "# R6 NudeNet 320n — Image inference test",
            "",
            "- Actual pinned ONNX model loaded with C# AIPrivacyGuard adapter.",
            "- The dataset may include externally sourced adult fashion, cosplay and fictional-character art. See source/license entries in results.json.",
            "- This verifies end-to-end candidate output and preview; does not assess real-world accuracy.",
            $"- Model hash: `{manifest.ModelSha256}`",
            $"- Threshold: {manifest.ScoreThreshold}, class NMS IoU: {manifest.NmsIouThreshold}",
            "",
            "| Test input | Detected regions | Elapsed (ms) |",
            "|---|---:|---:|"
        };
        foreach (var item in all)
        {
            using var row = JsonDocument.Parse(JsonSerializer.Serialize(item));
            var v = row.RootElement;
            markdown.Add($"| {v.GetProperty("id").GetString()} | {v.GetProperty("count").GetInt32()} | {v.GetProperty("elapsed_ms").GetDouble()} |");
        }
        markdown.Add("");
        markdown.Add("Reports include full JSON candidate coordinates. For externally sourced photos and copyrighted character art, source pixels and previews are intentionally excluded from the published artifact.");
        markdown.Add("All automatic mask previews are **simulations**, not approved privacy decisions.");
        markdown.Add("Zero candidates on these synthetic images never proves the model will detect true events.");
        await File.WriteAllLinesAsync(Path.Combine(reportFolder, "README.md"), markdown);
        Console.WriteLine($"VISUAL_EVAL_PASS: {cases.Length} synthetic images, {total} total candidate(s), JSON and previews saved.");
        return 0;
    }

    private static int[] ToBox(NormalizedRect box, int w, int h)
    {
        var p = box.ToPixels(w, h);
        return new[] { p.X, p.Y, p.Width, p.Height };
    }

    private static PixelBuffer DrawRectangles(PixelBuffer frame, IReadOnlyList<DetectionCandidate> candidates)
    {
        byte[] copy = (byte[])frame.Bgra.Clone();
        foreach (var candidate in candidates)
        {
            var rect = candidate.Bounds.ToPixels(frame.Width, frame.Height);
            int left = rect.X, top = rect.Y;
            int right = Math.Min(frame.Width - 1, rect.X + rect.Width - 1);
            int bottom = Math.Min(frame.Height - 1, rect.Y + rect.Height - 1);
            if (right < left || bottom < top) continue;
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    if (x - left > 2 && right - x > 2 && y - top > 2 && bottom - y > 2)
                        continue;
                    int i = (y * frame.Width + x) * 4;
                    copy[i] = 30; copy[i + 1] = 30; copy[i + 2] = 245; copy[i + 3] = 255;
                }
        }
        return new PixelBuffer(frame.Width, frame.Height, copy, copy: false);
    }
}
