using System.Security.Cryptography;
using PrivacyGuard.AI;
using PrivacyGuard.Core;

if (args.Length == 4 && args[1] == "--visual-fixtures")
    return await VisualEval.RunAsync(args[0], args[2], args[3]);

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run -- /path/to/320n.onnx");
    return 2;
}
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])));
var manifest = NudeNetPreset.Create();
if (!hash.Equals(manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Model weight SHA-256 mismatch.");
    return 3;
}
using var detector = new OnnxExposureDetector(manifest, args[0]);
// Synthetic solid images validate actual ONNX tensor layout, the postprocessor,
// and pixel coordinate mapping. This says NOTHING about detection accuracy.
foreach (byte value in new byte[] { 0, 90 })
{
    var bgra = new byte[320 * 320 * 4];
    for (int i = 0; i < bgra.Length; i += 4)
    {
        bgra[i] = value; bgra[i + 1] = value; bgra[i + 2] = value; bgra[i + 3] = 255;
    }
    var result = await detector.DetectAsync(new PixelBuffer(320, 320, bgra, copy: false),
        CancellationToken.None);
    if (result.Count > 200 || result.Any(x =>
        !double.IsFinite(x.Score) || x.Bounds.Width <= 0 || x.Bounds.Height <= 0 ||
        x.Bounds.X < 0 || x.Bounds.Y < 0 || x.Bounds.X + x.Bounds.Width > 1.000001 ||
        x.Bounds.Y + x.Bounds.Height > 1.000001))
        throw new InvalidDataException("Model returned malformed detections.");
    Console.WriteLine($"NudeNet ONNX inference executed: synthetic level {value}; {result.Count} candidate(s)");
}
Console.WriteLine("MODEL_SMOKE_PASS: ONNX loaded and two synthetic inferences completed (NOT accuracy validation).");
return 0;
