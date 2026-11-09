namespace PrivacyGuard.Core;

/// <summary>
/// Decode the 18-class NudeNet YOLOv8 raw detection head.
/// Channel-first [1,22,N] or [22,N] where 4 channels are cx,cy,w,h in
/// model-grid pixels and 18 channels are class confidence scores.
/// It is NOT a safety certification; no detections can still mean missed skin.
/// </summary>
public static class NudeNetYoloDecoder
{
    public const int ClassCount = 18;
    public const int Channels = 4 + ClassCount;
    public static readonly string[] Labels =
    [
        "FEMALE_GENITALIA_COVERED", "FACE_FEMALE", "BUTTOCKS_EXPOSED",
        "FEMALE_BREAST_EXPOSED", "FEMALE_GENITALIA_EXPOSED",
        "MALE_BREAST_EXPOSED", "ANUS_EXPOSED", "FEET_EXPOSED",
        "BELLY_COVERED", "FEET_COVERED", "ARMPITS_COVERED",
        "ARMPITS_EXPOSED", "FACE_MALE", "BELLY_EXPOSED",
        "MALE_GENITALIA_EXPOSED", "ANUS_COVERED",
        "FEMALE_BREAST_COVERED", "BUTTOCKS_COVERED"
    ];

    public static IReadOnlyList<DetectionCandidate> Decode(
        ReadOnlySpan<float> values, IReadOnlyList<int> dims, ModelManifest manifest,
        ModelInputTransform transform, int maxCandidates = 200)
    {
        manifest.Validate();
        if (maxCandidates is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        bool channelFirst;
        int n;
        if (dims.Count == 3 && dims[0] == 1 && dims[1] == Channels)
        { channelFirst = true; n = dims[2]; }
        else if (dims.Count == 2 && dims[0] == Channels)
        { channelFirst = true; n = dims[1]; }
        else if (dims.Count == 3 && dims[0] == 1 && dims[2] == Channels)
        { channelFirst = false; n = dims[1]; }
        else if (dims.Count == 2 && dims[1] == Channels)
        { channelFirst = false; n = dims[0]; }
        else throw new InvalidDataException("NudeNet expects a raw YOLO [1,22,N] or [1,N,22] head.");

        if (n is < 1 or > 100000 || (long)values.Length != (long)n * Channels)
            throw new InvalidDataException("Invalid NudeNet output dimensions.");

        float Get(int anchor, int channel) =>
            channelFirst ? values[channel * n + anchor] : values[anchor * Channels + channel];

        // Only allow category IDs explicitly named in the manifest: avoids
        // incorrectly masking faces, feet, stomach, etc.
        List<DetectionCandidate> boxes = new();
        for (int i = 0; i < n; i++)
        {
            int bestClass = -1;
            float bestScore = manifest.ScoreThreshold;
            for (int cls = 0; cls < ClassCount; cls++)
            {
                float score = Get(i, cls + 4);
                if (float.IsFinite(score) && score <= 1f && score >= bestScore)
                {
                    // YOLOv8 chooses the best class per anchor before
                    // subsequent optional filtering by category.
                    bestScore = score;
                    bestClass = cls;
                }
            }
            if (bestClass < 0 ||
                !manifest.Classes.TryGetValue(bestClass.ToString(), out string? category) ||
                !string.Equals(category, Labels[bestClass], StringComparison.Ordinal))
                continue;

            float cx = Get(i, 0), cy = Get(i, 1), w = Get(i, 2), h = Get(i, 3);
            if (!float.IsFinite(cx) || !float.IsFinite(cy) ||
                !float.IsFinite(w) || !float.IsFinite(h) || w <= 0 || h <= 0)
                continue;
            NormalizedRect rect;
            try
            {
                rect = transform.MapBox(cx - w / 2.0, cy - h / 2.0,
                    cx + w / 2.0, cy + h / 2.0, "xyxy_input_pixels");
            }
            catch (InvalidDataException) { continue; }
            if (rect.Width < 1e-6 || rect.Height < 1e-6) continue;
            boxes.Add(new DetectionCandidate(category, bestScore, rect));
        }

        var result = new List<DetectionCandidate>();
        foreach (var candidate in boxes.OrderByDescending(c => c.Score).Take(2000))
        {
            if (result.Any(kept => kept.Category == candidate.Category &&
                ModelTensorProcessor.IntersectionOverUnion(kept.Bounds, candidate.Bounds) >
                manifest.NmsIouThreshold)) continue;
            result.Add(candidate);
            if (result.Count >= maxCandidates) break;
        }
        return result;
    }
}
