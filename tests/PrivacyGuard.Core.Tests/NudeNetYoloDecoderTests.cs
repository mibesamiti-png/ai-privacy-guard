using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class NudeNetYoloDecoderTests
{
    private static ModelManifest Manifest() => new()
    {
        ModelId = "nudenet-test",
        ModelFile = "fixture.onnx",
        ModelSha256 = new string('a', 64),
        InputName = "images",
        OutputName = "output0",
        InputWidth = 320,
        InputHeight = 320,
        OutputFormat = "nudenet_yolov8_18class",
        Preprocessing = ModelPreprocessing.NudeNetSquare,
        Coordinates = "xyxy_input_pixels",
        ScoreThreshold = 0.25f,
        NmsIouThreshold = 0.45f,
        Classes = new Dictionary<string, string>
        {
            ["3"] = "FEMALE_BREAST_EXPOSED",
            ["0"] = "FEMALE_GENITALIA_COVERED"
        }
    };

    [Fact]
    public void ChannelFirstBoxesAreMappedAndOverlapsSuppressed()
    {
        var manifest = Manifest();
        const int n = 3;
        float[] head = new float[22 * n];
        void Put(int anchor, int channel, float value) => head[channel * n + anchor] = value;
        Put(0, 0, 160); Put(0, 1, 80); Put(0, 2, 64); Put(0, 3, 32);
        Put(0, 4 + 3, .92f);
        Put(1, 0, 160); Put(1, 1, 80); Put(1, 2, 64); Put(1, 3, 32);
        Put(1, 4 + 3, .85f);
        Put(2, 0, 20); Put(2, 1, 20); Put(2, 2, 16); Put(2, 3, 16);
        Put(2, 4 + 1, .99f); // face not in selected classes
        var t = new ModelInputTransform(320, 160, 320, 320, 1, 1, 0, 0);
        var result = NudeNetYoloDecoder.Decode(head, [1, 22, n], manifest, t);
        Assert.Single(result);
        Assert.Equal("FEMALE_BREAST_EXPOSED", result[0].Category);
        Assert.Equal(.4, result[0].Bounds.X, 8);
        Assert.Equal(.4, result[0].Bounds.Y, 8);
        Assert.Equal(.2, result[0].Bounds.Width, 8);
        Assert.Equal(.2, result[0].Bounds.Height, 8);
    }

    [Fact]
    public void AnchorFirstHeadAndCoveredClassDecode()
    {
        var m = Manifest();
        float[] row = new float[22];
        row[0] = 160; row[1] = 160; row[2] = 100; row[3] = 100;
        row[4] = .8f;
        var result = NudeNetYoloDecoder.Decode(row, [1, 1, 22], m,
            new ModelInputTransform(320, 320, 320, 320, 1, 1, 0, 0));
        Assert.Single(result);
        Assert.Equal("FEMALE_GENITALIA_COVERED", result[0].Category);
    }

    [Fact]
    public void InvalidShapeOrClassMappingRejected()
    {
        var m = Manifest();
        var t = new ModelInputTransform(320, 320, 320, 320, 1, 1, 0, 0);
        Assert.Throws<InvalidDataException>(() =>
            NudeNetYoloDecoder.Decode(new float[84 * 10], [1, 84, 10], m, t));
        m.Classes["3"] = "FACE_MALE";
        Assert.Throws<InvalidDataException>(() => m.Validate());
    }

    [Fact]
    public void EmptyScoresNeverClaimSensitiveDetection()
    {
        var m = Manifest();
        var result = NudeNetYoloDecoder.Decode(new float[22 * 10], [1, 22, 10], m,
            new ModelInputTransform(320, 320, 320, 320, 1, 1, 0, 0));
        Assert.Empty(result);
    }

    [Fact]
    public void NudeNetZeroPadsRightAndBottomBeforeResize()
    {
        // 2x1 BGRA: red pixel then green pixel.
        var image = new PixelBuffer(2, 1, new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 });
        var prepared = ModelPreprocessing.Prepare(image, 2, 2, ModelPreprocessing.NudeNetSquare);
        // Row 1 is zero padded, not a stretched copy of row 0.
        Assert.Equal(1f, prepared.Values[0]);
        Assert.Equal(1f, prepared.Values[4 + 1]);
        Assert.Equal(0f, prepared.Values[2]);
        Assert.Equal(0f, prepared.Values[4 + 2]);
        var mapped = prepared.Transform.MapBox(0, 0, 2, 1, "xyxy_input_pixels");
        Assert.Equal(1d, mapped.Width);
        Assert.Equal(1d, mapped.Height);
    }

    [Fact]
    public void WrongPreprocessModeIsRejectedByManifest()
    {
        var m = Manifest();
        m.Preprocessing = ModelPreprocessing.LetterboxBilinear;
        Assert.Throws<InvalidDataException>(() => m.Validate());
    }
}
