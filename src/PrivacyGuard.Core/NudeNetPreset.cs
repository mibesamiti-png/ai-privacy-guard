namespace PrivacyGuard.Core;

/// <summary>
/// The 320n profile uses the 18-class NudeNet YOLOv8 head. Class selection
/// intentionally excludes faces, bellies, armpits and feet; covered genitalia
/// and buttocks are review candidates only, NOT a panty-shot detector.
/// </summary>
public static class NudeNetPreset
{
    public const string ModelSha256 =
        "c15d8273adad2d0a92f014cc69ab2d6c311a06777a55545f2c4eb46f51911f0f";
    public const string PinnedUrl =
        "https://huggingface.co/deepghs/nudenet_onnx/resolve/44956a0587943016350c7815f9e0dec089d45cae/320n.onnx";

    public static ModelManifest Create() => new()
    {
        ModelId = "nudenet-320n-18class-r6",
        ModelFile = "320n.onnx",
        ModelSha256 = ModelSha256,
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
            ["0"] = "FEMALE_GENITALIA_COVERED",
            ["2"] = "BUTTOCKS_EXPOSED",
            ["3"] = "FEMALE_BREAST_EXPOSED",
            ["4"] = "FEMALE_GENITALIA_EXPOSED",
            ["6"] = "ANUS_EXPOSED",
            ["14"] = "MALE_GENITALIA_EXPOSED",
            ["17"] = "BUTTOCKS_COVERED"
        }
    };
}
