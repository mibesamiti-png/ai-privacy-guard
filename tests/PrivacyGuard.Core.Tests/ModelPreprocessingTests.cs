using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class ModelPreprocessingTests
{
    private static PixelBuffer Solid(int width, int height, byte b, byte g, byte r)
    {
        byte[] bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        { bgra[i] = b; bgra[i + 1] = g; bgra[i + 2] = r; bgra[i + 3] = 255; }
        return new PixelBuffer(width, height, bgra, copy: false);
    }

    [Fact]
    public void LetterboxProducesGrayPaddingAndKeepsCenterPixel()
    {
        var frame = Solid(4, 2, 20, 40, 60);
        var input = ModelPreprocessing.Prepare(frame, 8, 8, ModelPreprocessing.LetterboxBilinear);
        Assert.Equal(0, input.Transform.OffsetX);
        Assert.Equal(2, input.Transform.OffsetY);
        Assert.Equal(8 * 8 * 3, input.Values.Length);
        Assert.Equal(114f / 255f, input.Values[0], 6);
        Assert.Equal(60f / 255f, input.Values[3 * 8 + 2], 5);
    }

    [Fact]
    public void LetterboxMapsDetectionsBackToCorrectSourcePosition()
    {
        var input = ModelPreprocessing.Prepare(Solid(800, 400, 0, 0, 0), 640, 640,
            ModelPreprocessing.LetterboxBilinear);
        Assert.Equal(160, input.Transform.OffsetY);
        // Model box [160, 200, 480, 360] -> source-relative [0.25, 0.125, 0.50, 0.50].
        var rect = input.Transform.MapBox(160, 200, 480, 360, "xyxy_input_pixels");
        Assert.Equal(0.25, rect.X, 6);
        Assert.Equal(0.125, rect.Y, 6);
        Assert.Equal(0.50, rect.Width, 6);
        Assert.Equal(0.50, rect.Height, 6);
    }

    [Fact]
    public void NormalizedModelCoordinatesMapThroughPadding()
    {
        var input = ModelPreprocessing.Prepare(Solid(800, 400, 0, 0, 0), 640, 640,
            ModelPreprocessing.LetterboxBilinear);
        var rect = input.Transform.MapBox(.25, .3125, .75, .5625, "xyxy_normalized");
        Assert.Equal(.25, rect.X, 5);
        Assert.Equal(.125, rect.Y, 5);
        Assert.Equal(.5, rect.Width, 5);
        Assert.Equal(.5, rect.Height, 5);
    }

    [Fact]
    public void PaddingOnlyBoxIsDiscardedByDecoder()
    {
        var m = new ModelManifest
        {
            ModelId = "geometry-test", ModelFile = "model.onnx", ModelSha256 = new string('a', 64),
            InputWidth = 640, InputHeight = 640, Preprocessing = ModelPreprocessing.LetterboxBilinear,
            Classes = new Dictionary<string, string> { ["0"] = "TEST_REGION" }
        };
        var transform = ModelPreprocessing.Prepare(Solid(800, 400, 0, 0, 0), 640, 640,
            ModelPreprocessing.LetterboxBilinear).Transform;
        var rows = new float[] { 30, 10, 120, 60, 0.99f, 0 };
        var result = ModelTensorProcessor.DecodeSixColumns(rows, new[] { 1, 6 }, m, transform: transform);
        Assert.Empty(result);
    }

    [Fact]
    public void OddAspectRatioRoundTripUsesActualResizeDimensions()
    {
        var input = ModelPreprocessing.Prepare(Solid(103, 57, 0, 0, 0), 64, 64,
            ModelPreprocessing.LetterboxBilinear);
        var tx = input.Transform;
        double left = 20.5 / 103 * (tx.ScaleX * 103) + tx.OffsetX;
        double top = 12.5 / 57 * (tx.ScaleY * 57) + tx.OffsetY;
        double right = 81.5 / 103 * (tx.ScaleX * 103) + tx.OffsetX;
        double bottom = 49.5 / 57 * (tx.ScaleY * 57) + tx.OffsetY;
        var rect = tx.MapBox(left, top, right, bottom, "xyxy_input_pixels");
        Assert.Equal(20.5 / 103, rect.X, 6);
        Assert.Equal(12.5 / 57, rect.Y, 6);
        Assert.Equal(61.0 / 103, rect.Width, 6);
    }

    [Fact]
    public void ManifestRejectsUnknownPreprocessModes()
    {
        var m = new ModelManifest
        {
            ModelId = "test", ModelFile = "test.onnx", ModelSha256 = new string('a', 64),
            Preprocessing = "arbitrary", Classes = new Dictionary<string, string> { ["0"] = "TEST" }
        };
        Assert.Throws<InvalidDataException>(() => m.Validate());
    }
}
