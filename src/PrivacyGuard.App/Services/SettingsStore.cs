using System.IO;
using System.Text.Json;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

public sealed class GuardSettings
{
    public MaskStyle MaskStyle { get; set; } = PrivacyGuard.Core.MaskStyle.Pixelate;
    public int Strength { get; set; } = 20;
    public double CropMargin { get; set; } = 0.2;
}

public static class SettingsStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIPrivacyGuard", "settings.json");

    public static GuardSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<GuardSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new GuardSettings();
    }

    public static void Save(GuardSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
