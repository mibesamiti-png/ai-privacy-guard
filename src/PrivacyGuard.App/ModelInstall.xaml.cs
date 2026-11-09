using System.IO;
using System.Windows;
using PrivacyGuard.AI;
using PrivacyGuard.App.Services;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

/// <summary>Installed locally, not downloaded automatically without consent.</summary>
public partial class MainWindow
{
    private bool _modelInstalling;

    private void ActivateModelManifest(string filePath)
    {
        var (manifest, modelFile) = ModelManifest.LoadAndValidate(filePath);
        // Create replacement before disposing the working model.
        var replacement = new OnnxExposureDetector(manifest, modelFile);
        var old = _detector;
        _detector = replacement;
        _loadedManifest = manifest;
        _lastProbe = null;
        ModelProbeButton.IsEnabled = true;
        ExportProbeButton.IsEnabled = false;
        old?.Dispose();
        _vm.ModelStatus = $"モデル: {manifest.ModelId} / 前処理: {manifest.Preprocessing}（CPU/実験）";
        _vm.Status = "露出候補AIを読み込みました。画像・動画・一括解析で使用できます。誤検知・見逃しに注意。";
    }

    private void LoadLocalModelIfAvailable()
    {
        // Startup is offline. Never fetch a third-party model or send a user's
        // image to the network implicitly.
        if (!File.Exists(NudeNetModelInstaller.ManifestPath)) return;
        try { ActivateModelManifest(NudeNetModelInstaller.ManifestPath); }
        catch (Exception ex)
        {
            _vm.Status = "保存済みAIモデルの読み込み失敗: " + ex.Message;
        }
    }

    private async void InstallRecommendedModel_Click(object sender, RoutedEventArgs e)
    {
        if (_modelInstalling || _isDetecting || _batchRunning ||
            _videoScanning || _videoTracking || _videoExporting)
        {
            _vm.Status = "解析中はAIモデルを切り替えられません。";
            return;
        }
        var decision = MessageBox.Show(this,
            "第三者のNudeNet 320n ONNXモデルをダウンロードします。\n" +
            "・利用規約・元モデルのライセンスを確認してください（NudeNet原版はAGPL-3.0）。\n" +
            "・画像はPC内で処理し、画像を外部に送信しません。\n" +
            "・検出ミスがあるため、そのまま生配信に使わないでください。\n" +
            "・スカート内の下着を専用に識別するモデルではありません。\n\n" +
            "このモデルをPCにダウンロードしますか？",
            "露出検出AIを導入 — 第三者モデル", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes) return;

        _modelInstalling = true;
        InstallRecommendedModelButton.IsEnabled = false;
        LoadModelButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(rate =>
                _vm.Status = $"露出検出AIをインストール中… {rate:P0}");
            string installedManifest = await NudeNetModelInstaller.InstallAsync(progress);
            ActivateModelManifest(installedManifest);
            _vm.Status = "NudeNet AI導入完了。画像・動画・一括解析で候補を検出できます。判定は必ず確認してください。";
        }
        catch (Exception ex)
        {
            _vm.Status = "AIモデルの導入失敗: " + ex.Message;
            MessageBox.Show(this, ex.Message,
                "NudeNetモデル導入エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _modelInstalling = false;
            InstallRecommendedModelButton.IsEnabled = true;
            LoadModelButton.IsEnabled = true;
        }
    }
}
