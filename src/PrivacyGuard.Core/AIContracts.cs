using System.Security.Cryptography;
using System.Text.Json;

namespace PrivacyGuard.Core;

public sealed record DetectionCandidate(string Category, double Score, NormalizedRect Bounds);

public interface IExposureDetector : IDisposable
{
    string ModelId { get; }
    Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken cancellationToken);
}

/// <summary>
/// Strict experimental ONNX contract; does not assert an arbitrary model detects exposure.
/// Only [1,3,H,W] float RGB tensors and [1,N,6] or [N,6] float output
/// rows [x1,y1,x2,y2,confidence,classId] are accepted.
/// </summary>
public sealed class ModelManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string ModelId { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public string ModelSha256 { get; set; } = "";
    public string InputName { get; set; } = "images";
    public string OutputName { get; set; } = "detections";
    public int InputWidth { get; set; } = 640;
    public int InputHeight { get; set; } = 640;
    public string Coordinates { get; set; } = "xyxy_input_pixels";
    // Default remains backwards-compatible with the 6-column adapter.
    public string OutputFormat { get; set; } = "six_columns";
    public string Preprocessing { get; set; } = ModelPreprocessing.StretchNearest;
    public float ScoreThreshold { get; set; } = 0.5f;
    public float NmsIouThreshold { get; set; } = 0.5f;
    public Dictionary<string, string> Classes { get; set; } = new();

    public static (ModelManifest Manifest, string ModelPath) LoadAndValidate(string manifestPath)
    {
        var manifestFull = Path.GetFullPath(manifestPath);
        var json = File.ReadAllText(manifestFull);
        var manifest = JsonSerializer.Deserialize<ModelManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty manifest.");
        manifest.Validate();
        string baseDirectory = Path.GetDirectoryName(manifestFull)!;
        string modelFull = Path.GetFullPath(Path.Combine(baseDirectory, manifest.ModelFile));
        // Do not let a supplied manifest reference files outside its own folder.
        string basePrefix = Path.TrimEndingDirectorySeparator(baseDirectory) + Path.DirectorySeparatorChar;
        if (!modelFull.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model file must be beneath the manifest directory.");
        if (!File.Exists(modelFull)) throw new FileNotFoundException("ONNX model not found.", modelFull);
        using var stream = File.OpenRead(modelFull);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ONNX model SHA-256 does not match the manifest.");
        return (manifest, modelFull);
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unknown manifest version.");
        if (string.IsNullOrWhiteSpace(ModelId) || ModelId.Length > 100 ||
            ModelId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new InvalidDataException("Invalid model ID.");
        if (string.IsNullOrWhiteSpace(ModelFile) || Path.IsPathRooted(ModelFile) ||
            !ModelFile.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ModelFile must be a relative .onnx path.");
        if (ModelSha256.Length != 64 || !ModelSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("A valid ModelSha256 is required.");
        if (string.IsNullOrWhiteSpace(InputName) || string.IsNullOrWhiteSpace(OutputName))
            throw new InvalidDataException("Tensor names must be supplied.");
        if (InputWidth < 32 || InputHeight < 32 || InputWidth > 2048 || InputHeight > 2048)
            throw new InvalidDataException("Input dimensions must be between 32 and 2048.");
        if (Preprocessing is not (ModelPreprocessing.StretchNearest or ModelPreprocessing.LetterboxBilinear or ModelPreprocessing.NudeNetSquare))
            throw new InvalidDataException("Unsupported preprocessing mode.");
        if (OutputFormat is not ("six_columns" or "nudenet_yolov8_18class"))
            throw new InvalidDataException("Unsupported model output format.");
        if (OutputFormat == "nudenet_yolov8_18class")
        {
            if (Classes is null)
                throw new InvalidDataException("NudeNet class mapping is required.");
            if (InputWidth != InputHeight || Preprocessing != ModelPreprocessing.NudeNetSquare ||
                Coordinates != "xyxy_input_pixels")
                throw new InvalidDataException("NudeNet requires square padding with pixel coordinates.");
            foreach (var (id, label) in Classes)
            {
                if (!int.TryParse(id, out int index) || index < 0 ||
                    index >= NudeNetYoloDecoder.ClassCount ||
                    !string.Equals(label, NudeNetYoloDecoder.Labels[index], StringComparison.Ordinal))
                    throw new InvalidDataException("NudeNet class ID / label mapping mismatch.");
            }
        }
        if (Coordinates is not ("xyxy_input_pixels" or "xyxy_normalized"))
            throw new InvalidDataException("Unknown box coordinate convention.");
        if (!float.IsFinite(ScoreThreshold) || ScoreThreshold is < 0 or > 1 ||
            !float.IsFinite(NmsIouThreshold) || NmsIouThreshold is <= 0 or > 1)
            throw new InvalidDataException("Invalid score or overlap threshold.");
        if (Classes is null || Classes.Count == 0 || Classes.Count > 64)
            throw new InvalidDataException("The model must declare its output classes.");
        foreach (var (index, label) in Classes)
        {
            if (!int.TryParse(index, out int classId) || classId < 0 || classId > 10000 ||
                string.IsNullOrWhiteSpace(label) || label.Length > 64 ||
                label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                throw new InvalidDataException("Invalid class mapping.");
        }
    }
}

public static class ModelTensorProcessor
{
    /// <summary>Backward-compatible stretched RGB input helper for R2.0 callers.</summary>
    public static float[] ToRgbNchw(PixelBuffer image, int width, int height) =>
        ModelPreprocessing.Prepare(image, width, height, ModelPreprocessing.StretchNearest).Values;

    /// <summary>Only accepts pre-decoded 6-column outputs, not YOLO raw heads.</summary>
    public static IReadOnlyList<DetectionCandidate> DecodeSixColumns(
        ReadOnlySpan<float> values, IReadOnlyList<int> dimensions, ModelManifest manifest,
        int maxCandidates = 200, ModelInputTransform? transform = null)
    {
        manifest.Validate();
        int rows = dimensions.Count switch
        {
            2 when dimensions[1] == 6 => dimensions[0],
            3 when dimensions[0] == 1 && dimensions[2] == 6 => dimensions[1],
            _ => throw new InvalidDataException("Expected [N,6] or [1,N,6] output.")
        };
        if (rows < 0 || rows > 200_000 || values.Length != (long)rows * 6)
            throw new InvalidDataException("Output tensor size does not match its dimensions.");
        if (maxCandidates < 1 || maxCandidates > 2000)
            throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        var candidates = new List<DetectionCandidate>();
        for (int i = 0; i < rows; i++)
        {
            var row = values.Slice(i * 6, 6);
            if (!float.IsFinite(row[0]) || !float.IsFinite(row[1]) ||
                !float.IsFinite(row[2]) || !float.IsFinite(row[3]) ||
                !float.IsFinite(row[4]) || !float.IsFinite(row[5])) continue;
            float confidence = row[4];
            if (confidence < manifest.ScoreThreshold || confidence > 1 || confidence < 0) continue;
            float classValue = row[5];
            int classId = (int)classValue;
            if (MathF.Abs(classValue - classId) > 0.001f ||
                !manifest.Classes.TryGetValue(classId.ToString(), out var category)) continue;
            double scaleX = manifest.Coordinates == "xyxy_input_pixels" ? manifest.InputWidth : 1;
            double scaleY = manifest.Coordinates == "xyxy_input_pixels" ? manifest.InputHeight : 1;
            double x1 = row[0] / scaleX;
            double y1 = row[1] / scaleY;
            double x2 = row[2] / scaleX;
            double y2 = row[3] / scaleY;
            if (x2 <= x1 || y2 <= y1) continue;
            var bounds = transform is null
                ? new NormalizedRect(x1, y1, x2 - x1, y2 - y1).Clamp()
                : transform.Value.MapBox(row[0], row[1], row[2], row[3], manifest.Coordinates);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            candidates.Add(new DetectionCandidate(category, confidence, bounds));
        }
        // NMS: filter only overlapping regions of the *same category*.
        var result = new List<DetectionCandidate>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Score))
        {
            if (result.Any(kept => kept.Category == candidate.Category &&
                IntersectionOverUnion(kept.Bounds, candidate.Bounds) > manifest.NmsIouThreshold)) continue;
            result.Add(candidate);
            if (result.Count == maxCandidates) break;
        }
        return result;
    }

    public static double IntersectionOverUnion(NormalizedRect a, NormalizedRect b)
    {
        a = a.Clamp(); b = b.Clamp();
        double left = Math.Max(a.X, b.X), top = Math.Max(a.Y, b.Y);
        double right = Math.Min(a.X + a.Width, b.X + b.Width);
        double bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double areaA = a.Width * a.Height, areaB = b.Width * b.Height;
        double union = areaA + areaB - intersection;
        return union > 0 ? intersection / union : 0;
    }
}
