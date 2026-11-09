using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class ModelTensorTests
{
    private static ModelManifest ValidManifest() => new()
    {
        ModelId = "test-detector",
        ModelFile = "test.onnx",
        ModelSha256 = new string('a', 64),
        InputName = "images",
        OutputName = "detections",
        InputWidth = 100,
        InputHeight = 100,
        Coordinates = "xyxy_input_pixels",
        ScoreThreshold = 0.5f,
        NmsIouThreshold = 0.5f,
        Classes = new Dictionary<string, string>
        {
            ["0"] = "UNDERWEAR_EXPOSURE",
            ["1"] = "CLOTHING_FAILURE"
        }
    };

    [Fact]
    public void RgbPreprocessingConvertsBgraToChannels()
    {
        var frame = new PixelBuffer(1, 1, new byte[] { 10, 20, 30, 255 });
        var tensor = ModelTensorProcessor.ToRgbNchw(frame, 1, 1);
        Assert.Equal(30f / 255f, tensor[0]);
        Assert.Equal(20f / 255f, tensor[1]);
        Assert.Equal(10f / 255f, tensor[2]);
    }

    [Fact]
    public void DecoderFiltersScoresAndSuppressesDuplicates()
    {
        float[] rows =
        {
            10, 10, 50, 50, .95f, 0,
            11, 11, 51, 51, .90f, 0,
            10, 10, 50, 50, .85f, 1,
            10, 10, 50, 50, .30f, 0
        };
        var result = ModelTensorProcessor.DecodeSixColumns(rows, new[] { 1, 4, 6 }, ValidManifest());
        Assert.Equal(2, result.Count);
        Assert.Equal("UNDERWEAR_EXPOSURE", result[0].Category);
        Assert.Equal("CLOTHING_FAILURE", result[1].Category);
        Assert.Equal(0.1, result[0].Bounds.X, 4);
        Assert.Equal(0.4, result[0].Bounds.Width, 4);
    }

    [Fact]
    public void DecoderRejectsInvalidAndNonFiniteRows()
    {
        float[] rows =
        {
            float.NaN, 10, 50, 50, 1, 0,
            10, 10, 50, 50, 1, 9,
            50, 50, 10, 10, 1, 0,
            0, 0, 10, 10, .75f, 0
        };
        var result = ModelTensorProcessor.DecodeSixColumns(rows, new[] { 4, 6 }, ValidManifest());
        Assert.Single(result);
    }

    [Fact]
    public void DecoderClipsRegionsToFrame()
    {
        var result = ModelTensorProcessor.DecodeSixColumns(
            new float[] { -20, -30, 30, 130, 0.9f, 0 }, new[] { 1, 6 }, ValidManifest());
        Assert.Single(result);
        Assert.Equal(0.0, result[0].Bounds.X);
        Assert.Equal(0.0, result[0].Bounds.Y);
        Assert.Equal(0.3, result[0].Bounds.Width, 5);
        Assert.Equal(1.0, result[0].Bounds.Height, 5);
    }

    [Fact]
    public void DecoderFailsClosedOnUnknownOutputLayout()
    {
        Assert.Throws<InvalidDataException>(() =>
            ModelTensorProcessor.DecodeSixColumns(new float[12], new[] { 1, 6, 2 }, ValidManifest()));
    }

    [Fact]
    public void ManifestRejectsUnmappedAndUnsupportedModels()
    {
        var m = ValidManifest();
        m.Coordinates = "cxcywh";
        Assert.Throws<InvalidDataException>(() => m.Validate());
        m.Coordinates = "xyxy_normalized";
        m.Classes = new();
        Assert.Throws<InvalidDataException>(() => m.Validate());
    }

    [Fact]
    public void ManifestRejectsTraversalBeforeAccessingModel()
    {
        string folder = Path.Combine(Path.GetTempPath(), "apg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var m = ValidManifest();
            m.ModelFile = "../outside.onnx";
            var path = Path.Combine(folder, "manifest.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(m));
            Assert.Throws<InvalidDataException>(() => ModelManifest.LoadAndValidate(path));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void IoUZeroForDisjointBoxes()
    {
        Assert.Equal(0, ModelTensorProcessor.IntersectionOverUnion(
            new NormalizedRect(0, 0, .2, .2), new NormalizedRect(.8, .8, .2, .2)));
    }
}
