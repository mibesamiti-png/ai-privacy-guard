using System.IO;
using Path = System.IO.Path;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PrivacyGuard.App.Services;
using PrivacyGuard.AI;
using PrivacyGuard.App.ViewModels;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly HistoryRepository _history = new();
    private readonly GuardSettings _settings = SettingsStore.Load();
    private readonly Stack<(Guid eventId, NormalizedRect oldBounds)> _undoRegions = new();
    private PixelBuffer? _image;
    private PixelBuffer? _previewImage;
    private Guid _sourceId;
    private string? _imagePath;
    private bool _isLoading;
    private bool _manualMode;
    private IExposureDetector? _detector;
    private ModelManifest? _loadedManifest;
    private ModelProbe.ProbeReport? _lastProbe;
    private bool _isDetecting;
    private CancellationTokenSource? _detectCts;

    private enum DragMode { None, Create, Move, Resize }
    private DragMode _dragMode;
    private ResizeAnchor _resizeAnchor;
    private Point _dragStartNormalized;
    private NormalizedRect _dragOriginal;
    private NormalizedRect? _dragPreview;
    private ReviewItem? _dragItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        MaskStyleCombo.SelectedIndex = (int)_settings.MaskStyle;
        StrengthSlider.Value = _settings.Strength;
        InitializeVideoTab();
        InitializeBatchTab();
        InitializeBatchExportTab();
    }

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isDetecting) { _vm.Status = "解析中は画像を切り替えられません。先に解析を停止してください。"; return; }
        var dialog = new OpenFileDialog
        {
            Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|すべてのファイル|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await OpenImagePathAsync(dialog.FileName);
    }

    private async Task OpenImagePathAsync(string filePath)
    {
        if (_isLoading || _isDetecting) return;
        _isLoading = true;
        _vm.Status = "画像を読み込み中…";
        ResetDrag();
        try
        {
            _image = null;
            _previewImage = null;
            _imagePath = null;
            _vm.Candidates.Clear();
            _vm.Selected = null;
            _vm.MainImage = null;
            _vm.ProcessedImage = null;
            _vm.CropImage = null;
            _undoRegions.Clear();
            var pixels = await ImageCodec.ReadAsync(filePath);
            await using var stream = File.OpenRead(filePath);
            byte[] sha = await SHA256.HashDataAsync(stream);
            string hash = Convert.ToHexString(sha);
            _sourceId = _history.GetOrCreateSource(filePath, hash, pixels.Width, pixels.Height);
            _image = pixels;
            _previewImage = PreviewProcessor.Downsample(pixels);
            _imagePath = Path.GetFullPath(filePath);
            _vm.SourcePath = _imagePath;
            _vm.MainImage = ImageCodec.ToBitmap(pixels);
            foreach (var record in _history.GetDetections(_sourceId))
                _vm.Candidates.Add(MakeReviewItem(record));
            if (_vm.Candidates.Count > 0) _vm.Selected = _vm.Candidates[0];
            RefreshOverlay();
            RefreshCrop();
            RefreshProcessedPreview();
            _vm.Status = $"読み込み完了：{pixels.Width}×{pixels.Height} / 保存済み候補 {_vm.Candidates.Count}件";
        }
        catch (Exception ex)
        {
            _vm.Status = "読み込み失敗：" + ex.Message;
            MessageBox.Show(this, ex.Message, "画像の読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _isLoading = false; }
    }

    private ReviewItem MakeReviewItem(DetectionRecord record)
    {
        var item = new ReviewItem(record);
        RefreshThumbnail(item);
        return item;
    }

    private void RefreshThumbnail(ReviewItem item)
    {
        if (_image is null) return;
        try
        {
            item.Thumbnail = ImageCodec.ToBitmap(PreviewProcessor.Thumbnail(
                _image, item.Data.Bounds, 112, 84, _settings.CropMargin));
        }
        catch (InvalidOperationException) { item.Thumbnail = null; }
    }

    private void LoadModel_Click(object sender, RoutedEventArgs e)
    {
        if (_isDetecting) { _vm.Status = "解析を停止してからモデルを変更してください。"; return; }
        var dialog = new OpenFileDialog
        {
            Title = "ONNXモデル定義（JSON）を選択",
            Filter = "モデル定義|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var (manifest, modelFile) = ModelManifest.LoadAndValidate(dialog.FileName);
            var replacement = new OnnxExposureDetector(manifest, modelFile);
            var previous = _detector;
            _detector = replacement;
            _loadedManifest = manifest;
            _lastProbe = null;
            ModelProbeButton.IsEnabled = true;
            ExportProbeButton.IsEnabled = false;
            previous?.Dispose();
            _vm.ModelStatus = $"モデル: {manifest.ModelId} / 前処理: {manifest.Preprocessing}（外部ONNX・CPU実験）";
            _vm.Status = "モデルの読み込みが完了しました。対応カテゴリや検出精度は未検証です。";
        }
        catch (Exception ex)
        {
            _vm.Status = "モデル読み込み失敗: " + ex.Message;
            MessageBox.Show(this, ex.Message, "ONNXモデル読み込み失敗", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void ModelProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_isDetecting || _isLoading) return;
        if (_detector is null || _loadedManifest is null)
        {
            _vm.Status = "先にモデル定義JSONを読み込んでください。";
            return;
        }
        _isDetecting = true;
        _detectCts = new CancellationTokenSource();
        DetectButton.IsEnabled = false;
        LoadModelButton.IsEnabled = false;
        ModelProbeButton.IsEnabled = false;
        ExportProbeButton.IsEnabled = false;
        _lastProbe = null;
        _vm.Status = "人工的な画像2枚でONNXの接続診断中…（検出精度の評価ではありません）";
        try
        {
            var report = await ModelProbe.RunAsync(_loadedManifest, _detector, _detectCts.Token);
            _detectCts.Token.ThrowIfCancellationRequested();
            _lastProbe = report;
            ExportProbeButton.IsEnabled = true;
            string details = string.Join(" / ", report.Cases.Select(c =>
                $"{c.Pattern}: {c.Status} {c.ElapsedMs:0.##}ms 候補{c.CandidateCount}"));
            _vm.Status = $"モデル接続診断 {report.Result}: {details}。人工画像による入出力確認のみです。";
        }
        catch (OperationCanceledException)
        {
            _vm.Status = "モデル接続診断を中止しました。";
        }
        catch (Exception ex)
        {
            _vm.Status = "モデル診断の実行に失敗: " + ex.Message;
        }
        finally
        {
            _isDetecting = false;
            DetectButton.IsEnabled = true;
            LoadModelButton.IsEnabled = true;
            ModelProbeButton.IsEnabled = _detector is not null;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    private void ExportProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_lastProbe is null) { _vm.Status = "モデル診断を実行してください。"; return; }
        var dialog = new SaveFileDialog
        {
            Filter = "JSON診断レポート|*.json",
            DefaultExt = ".json",
            FileName = "apg_model_probe.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(_lastProbe, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _vm.Status = "モデル接続レポートを保存しました。人物画像やファイルパスは含みません。";
        }
        catch (Exception ex)
        {
            _vm.Status = "診断レポート保存失敗: " + ex.Message;
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DetectImage_Click(object sender, RoutedEventArgs e)
    {
        if (_isDetecting || _isLoading) return;
        if (_image is null) { NeedImage(); return; }
        if (_detector is null)
        {
            _vm.Status = "モデル未選択です。先にモデル定義JSONを読み込んでください。";
            return;
        }
        var detector = _detector;
        var sourceId = _sourceId;
        var sourceImage = _image;
        _detectCts = new CancellationTokenSource();
        _isDetecting = true;
        DetectButton.IsEnabled = false;
        LoadModelButton.IsEnabled = false;
        ModelProbeButton.IsEnabled = false;
        _vm.Status = "ONNX推論を実行中（CPU）…";
        try
        {
            var predictions = await detector.DetectAsync(sourceImage, _detectCts.Token);
            _detectCts.Token.ThrowIfCancellationRequested();
            if (sourceId != _sourceId || !ReferenceEquals(sourceImage, _image))
                throw new OperationCanceledException("画像が変更されました。");
            var current = _vm.Candidates.Select(x => x.Data).ToList();
            var records = new List<DetectionRecord>();
            foreach (var candidate in predictions)
            {
                // Repeated runs must not flood the queue with the same candidate.
                if (current.Concat(records).Any(x => x.Category == candidate.Category &&
                    x.Origin == "onnx:" + detector.ModelId &&
                    ModelTensorProcessor.IntersectionOverUnion(x.Bounds, candidate.Bounds) >= 0.80))
                    continue;
                records.Add(new DetectionRecord(Guid.NewGuid(), sourceId, candidate.Category,
                    candidate.Score, candidate.Bounds, ReviewDecision.Pending,
                    "onnx:" + detector.ModelId, DateTimeOffset.UtcNow));
            }
            _history.InsertDetections(records);
            foreach (var record in records) _vm.Candidates.Add(MakeReviewItem(record));
            if (records.Count > 0) _vm.Selected = _vm.Candidates[^1];
            RefreshOverlay();
            RefreshCrop();
            RefreshProcessedPreview();
            _vm.Status = $"推論候補 {predictions.Count}件、新規登録 {records.Count}件。目視で確認してください。";
        }
        catch (OperationCanceledException)
        {
            _vm.Status = "解析を中止しました。候補は追加していません。";
        }
        catch (Exception ex)
        {
            _vm.Status = "AI解析に失敗: " + ex.Message;
            MessageBox.Show(this, ex.Message, "推論エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isDetecting = false;
            DetectButton.IsEnabled = true;
            LoadModelButton.IsEnabled = true;
            ModelProbeButton.IsEnabled = _detector is not null;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    private void CancelDetection_Click(object sender, RoutedEventArgs e)
    {
        if (_isDetecting) _detectCts?.Cancel();
    }

    private void ExportFeedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var summary = ReviewFeedbackAnalyzer.Summarize(_history.GetAllModelDetections());
            if (summary.Count == 0)
            {
                _vm.Status = "AI候補の履歴はまだありません。模擬候補と手動候補は集計対象外です。";
                return;
            }
            var dialog = new SaveFileDialog
            {
                Filter = "CSV集計|*.csv",
                DefaultExt = ".csv",
                FileName = "ai_privacy_guard_feedback.csv",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            // UTF-8 with BOM for Japanese Excel. Export deliberately excludes
            // source file paths, thumbnails and any intimate image pixels.
            File.WriteAllText(dialog.FileName, ReviewFeedbackAnalyzer.ToCsv(summary),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _vm.Status = $"AIレビュー集計 {summary.Count}カテゴリを書き出しました。判定傾向であり、検出精度や見逃し率ではありません。";
        }
        catch (Exception ex)
        {
            _vm.Status = "レポートの保存に失敗: " + ex.Message;
            MessageBox.Show(this, ex.Message, "レビュー集計エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddMock_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) { NeedImage(); return; }
        // Deliberately artificial rectangle: no model/inference is used.
        var record = new DetectionRecord(Guid.NewGuid(), _sourceId, "MOCK_CANDIDATE", null,
            new NormalizedRect(0.35, 0.35, 0.25, 0.20), ReviewDecision.Pending,
            "mock", DateTimeOffset.UtcNow);
        AddRecord(record);
        _vm.Status = "模擬検出を追加しました。実際のAI判定ではありません。";
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) { NeedImage(); return; }
        _manualMode = true;
        PreviewCanvas.Cursor = Cursors.Cross;
        _vm.Status = "元画像上でドラッグして隠す候補範囲を指定してください。";
    }

    private void AddRecord(DetectionRecord record)
    {
        _history.InsertDetection(record);
        var item = MakeReviewItem(record);
        _vm.Candidates.Add(item);
        _vm.Selected = item;
        RefreshOverlay();
        RefreshCrop();
        RefreshProcessedPreview();
    }

    private void Approve_Click(object sender, RoutedEventArgs e) => SetDecision(ReviewDecision.Approved);
    private void Reject_Click(object sender, RoutedEventArgs e) => SetDecision(ReviewDecision.Rejected);
    private void Defer_Click(object sender, RoutedEventArgs e) => SetDecision(ReviewDecision.Deferred);

    private void SetDecision(ReviewDecision decision)
    {
        if (_vm.Selected is not { } item)
        {
            _vm.Status = "候補を1件選択してください。";
            return;
        }
        try
        {
            _history.UpdateDecision(item.Data.Id, decision);
            item.SetDecision(decision);
            _vm.Status = $"判定を保存しました：{item.DecisionText}。加工は「隠す」の候補のみ対象です。";
            RefreshOverlay();
            RefreshProcessedPreview();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "判定保存エラー"); }
    }

    private void UndoRegion_Click(object sender, RoutedEventArgs e)
    {
        if (_undoRegions.Count == 0) { _vm.Status = "元に戻せる範囲編集がありません。"; return; }
        var (id, bounds) = _undoRegions.Peek();
        var item = _vm.Candidates.FirstOrDefault(x => x.Data.Id == id);
        if (item is null) { _undoRegions.Pop(); return; }
        try
        {
            _history.UpdateBounds(id, bounds);
            item.SetBounds(bounds);
            RefreshThumbnail(item);
            _undoRegions.Pop();
            _vm.Selected = item;
            RefreshOverlay();
            RefreshCrop();
            RefreshProcessedPreview();
            _vm.Status = "直前の範囲編集を元に戻しました。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "編集取り消しエラー"); }
    }

    private async void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null || _imagePath is null) { NeedImage(); return; }
        var approved = _vm.Candidates.Select(x => x.Data)
            .Where(x => x.Decision == ReviewDecision.Approved).ToList();
        if (approved.Count == 0)
        {
            _vm.Status = "隠す対象がありません。確認キューから候補を選び「隠す」を押してください。";
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "PNG画像|*.png",
            DefaultExt = ".png",
            FileName = Path.GetFileNameWithoutExtension(_imagePath) + "_guarded.png",
            InitialDirectory = Path.GetDirectoryName(_imagePath),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var target = Path.GetFullPath(dialog.FileName);
        if (string.Equals(_imagePath, target, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "入力画像と同じ場所・ファイル名への上書きは禁止です。",
                "元データ保護", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var style = CurrentMaskStyle();
        int strength = (int)StrengthSlider.Value;
        try
        {
            _vm.Status = "加工画像を書き出しています…";
            PixelBuffer buffer = _image;
            await Task.Run(() => ImageCodec.WritePng(target,
                MaskProcessor.Apply(buffer, approved, style, strength, _settings.CropMargin)));
            _vm.Status = $"保存完了：{target} / 加工対象 {approved.Count}件。元画像は保持しました。";
        }
        catch (Exception ex)
        {
            _vm.Status = "保存に失敗しました：" + ex.Message;
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private MaskStyle CurrentMaskStyle() =>
        (MaskStyle)Math.Clamp(MaskStyleCombo.SelectedIndex, 0, 2);

    private void MaskStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshProcessedPreview();
    private void StrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RefreshProcessedPreview();

    private void RefreshProcessedPreview()
    {
        if (_previewImage is null || MaskStyleCombo is null || StrengthSlider is null) return;
        try
        {
            var approved = _vm.Candidates.Select(x => x.Data)
                .Where(x => x.Decision == ReviewDecision.Approved).ToList();
            // Preview is bounded to ~960x720, so scale the cell/radius relative
            // to the exported full-resolution frame. This is an approximation.
            double ratio = _image is null ? 1 : _previewImage.Width / (double)_image.Width;
            int previewStrength = Math.Max(4, (int)Math.Round(StrengthSlider.Value * ratio));
            var processed = approved.Count == 0 ? _previewImage :
                MaskProcessor.Apply(_previewImage, approved, CurrentMaskStyle(),
                    previewStrength, _settings.CropMargin);
            _vm.ProcessedImage = ImageCodec.ToBitmap(processed);
        }
        catch (Exception ex) { _vm.Status = "加工プレビュー生成エラー：" + ex.Message; }
    }

    private void Zoom1_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void Zoom2_Click(object sender, RoutedEventArgs e) => SetZoom(2);
    private void Zoom4_Click(object sender, RoutedEventArgs e) => SetZoom(4);
    private void Zoom8_Click(object sender, RoutedEventArgs e) => SetZoom(8);

    private void SetZoom(int zoom)
    {
        _vm.Zoom = zoom;
        CropImageView.LayoutTransform = new ScaleTransform(zoom, zoom);
    }

    private void Candidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCrop();
        RefreshOverlay();
    }

    private void RefreshCrop()
    {
        if (_image is null || _vm.Selected is not { } item)
        {
            _vm.CropImage = null;
            return;
        }
        try { _vm.CropImage = ImageCodec.ToBitmap(_image.Crop(item.Data.Bounds, _settings.CropMargin)); }
        catch (InvalidOperationException) { _vm.CropImage = null; }
    }

    private (double scale, double left, double top) GetImageTransform()
    {
        if (_image is null || PreviewCanvas.ActualWidth <= 0 || PreviewCanvas.ActualHeight <= 0)
            return (0, 0, 0);
        double scale = Math.Min(PreviewCanvas.ActualWidth / _image.Width,
                                PreviewCanvas.ActualHeight / _image.Height);
        return (scale, (PreviewCanvas.ActualWidth - _image.Width * scale) / 2,
                       (PreviewCanvas.ActualHeight - _image.Height * scale) / 2);
    }

    private Point ToNormalized(Point p)
    {
        if (_image is null) return new Point(0, 0);
        var (scale, left, top) = GetImageTransform();
        if (scale <= 0) return new Point(0, 0);
        return new Point(
            Math.Clamp((p.X - left) / (_image.Width * scale), 0, 1),
            Math.Clamp((p.Y - top) / (_image.Height * scale), 0, 1));
    }

    private void RefreshOverlay()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        if (_image is null) return;
        var (s, left, top) = GetImageTransform();
        if (s <= 0) return;
        foreach (var item in _vm.Candidates)
        {
            var r = item == _dragItem && _dragPreview is not null ? _dragPreview.Value : item.Data.Bounds;
            AddRectangle(r.Clamp(), s, left, top, item == _vm.Selected ? Brushes.Orange : Brushes.DeepSkyBlue,
                item == _vm.Selected ? 2.5 : 1.6, item == _vm.Selected);
        }
        if (_dragMode == DragMode.Create && _dragPreview is not null)
            AddRectangle(_dragPreview.Value, s, left, top, Brushes.LimeGreen, 2, false);
    }

    private void AddRectangle(NormalizedRect r, double scale, double left, double top,
        Brush stroke, double thickness, bool withHandles)
    {
        if (_image is null || r.Width <= 0 || r.Height <= 0) return;
        double x1 = left + r.X * _image.Width * scale;
        double y1 = top + r.Y * _image.Height * scale;
        double x2 = x1 + r.Width * _image.Width * scale;
        double y2 = y1 + r.Height * _image.Height * scale;
        var shape = new Rectangle
        {
            Width = x2 - x1, Height = y2 - y1, StrokeThickness = thickness,
            Stroke = stroke, Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        Canvas.SetLeft(shape, x1);
        Canvas.SetTop(shape, y1);
        PreviewCanvas.Children.Add(shape);
        if (!withHandles) return;
        foreach (var (x, y) in new[] { (x1, y1), (x2, y1), (x1, y2), (x2, y2) })
        {
            var handle = new Rectangle
            {
                Width = 9, Height = 9, Fill = Brushes.White,
                Stroke = Brushes.DarkOrange, StrokeThickness = 1.5, IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, x - 4.5);
            Canvas.SetTop(handle, y - 4.5);
            PreviewCanvas.Children.Add(handle);
        }
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshOverlay();

    private bool TryPickCandidate(Point screenPoint, out ReviewItem? item, out DragMode mode, out ResizeAnchor corner)
    {
        item = null;
        mode = DragMode.None;
        corner = ResizeAnchor.BottomRight;
        if (_image is null) return false;
        var (s, _, _) = GetImageTransform();
        if (s <= 0) return false;
        var p = ToNormalized(screenPoint);
        double tx = 10 / (_image.Width * s), ty = 10 / (_image.Height * s);
        var selected = _vm.Selected;
        IEnumerable<ReviewItem> ordered = selected is null ? _vm.Candidates.Reverse() :
            new[] { selected }.Concat(_vm.Candidates.Reverse().Where(x => x != selected));
        foreach (var candidate in ordered)
        {
            var r = candidate.Data.Bounds.Clamp();
            var corners = new[]
            {
                (r.X, r.Y, ResizeAnchor.TopLeft),
                (r.X + r.Width, r.Y, ResizeAnchor.TopRight),
                (r.X, r.Y + r.Height, ResizeAnchor.BottomLeft),
                (r.X + r.Width, r.Y + r.Height, ResizeAnchor.BottomRight)
            };
            foreach (var (cx, cy, anchor) in corners)
            {
                if (Math.Abs(p.X - cx) <= tx && Math.Abs(p.Y - cy) <= ty)
                {
                    item = candidate; mode = DragMode.Resize; corner = anchor; return true;
                }
            }
            if (p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height)
            {
                item = candidate; mode = DragMode.Move; return true;
            }
        }
        return false;
    }

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_image is null) return;
        var screenPoint = e.GetPosition(PreviewCanvas);
        var (s, _, _) = GetImageTransform();
        if (s <= 0) return;
        _dragStartNormalized = ToNormalized(screenPoint);
        _dragPreview = null;
        _dragItem = null;
        if (_manualMode)
        {
            _dragMode = DragMode.Create;
            _dragOriginal = new NormalizedRect(_dragStartNormalized.X, _dragStartNormalized.Y, 0, 0);
        }
        else if (TryPickCandidate(screenPoint, out var item, out var mode, out var anchor) && item is not null)
        {
            _vm.Selected = item;
            _dragItem = item;
            _dragMode = mode;
            _resizeAnchor = anchor;
            _dragOriginal = item.Data.Bounds.Clamp();
            _dragPreview = _dragOriginal;
            PreviewCanvas.Cursor = mode == DragMode.Move ? Cursors.SizeAll : Cursors.SizeNWSE;
        }
        else return;
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == DragMode.None || _image is null) return;
        var current = ToNormalized(e.GetPosition(PreviewCanvas));
        _dragPreview = _dragMode switch
        {
            DragMode.Create => RegionEditor.FromPoints(_dragStartNormalized.X, _dragStartNormalized.Y, current.X, current.Y),
            DragMode.Move => RegionEditor.Move(_dragOriginal, current.X - _dragStartNormalized.X, current.Y - _dragStartNormalized.Y),
            DragMode.Resize => RegionEditor.Resize(_dragOriginal, _resizeAnchor, current.X, current.Y,
                4.0 / _image.Width, 4.0 / _image.Height),
            _ => null
        };
        RefreshOverlay();
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.None || _image is null) return;
        PreviewCanvas_MouseMove(sender, e);
        var result = _dragPreview;
        var mode = _dragMode;
        var item = _dragItem;
        var original = _dragOriginal;
        ResetDrag();
        if (result is null) { RefreshOverlay(); return; }
        var bounds = result.Value.Clamp();
        if (bounds.Width * _image.Width < 4 || bounds.Height * _image.Height < 4)
        {
            _vm.Status = "範囲が小さすぎます。もう一度ドラッグしてください。";
            RefreshOverlay(); return;
        }
        try
        {
            if (mode == DragMode.Create)
            {
                AddRecord(DetectionRecord.Manual(_sourceId, bounds));
                _vm.Status = "手動候補を保存しました。切り取りを確認してから「隠す」を選択してください。";
            }
            else if (item is not null && bounds != original)
            {
                _history.UpdateBounds(item.Data.Id, bounds);
                _undoRegions.Push((item.Data.Id, original));
                item.SetBounds(bounds);
                RefreshThumbnail(item);
                RefreshCrop();
                RefreshProcessedPreview();
                _vm.Status = "範囲編集を保存しました。必要なら「範囲編集を元に戻す」で取り消せます。";
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "範囲保存エラー"); }
        RefreshOverlay();
    }

    private void PreviewCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // An outside click or window deactivation discards an unfinished edit.
        if (_dragMode == DragMode.None) return;
        ResetDrag();
        RefreshOverlay();
    }

    private void ResetDrag()
    {
        _dragMode = DragMode.None;
        _dragItem = null;
        _dragPreview = null;
        _manualMode = false;
        if (PreviewCanvas is not null)
        {
            PreviewCanvas.Cursor = Cursors.Arrow;
            if (PreviewCanvas.IsMouseCaptured) PreviewCanvas.ReleaseMouseCapture();
        }
    }

    private void NeedImage() => _vm.Status = "最初に「画像を開く」から画像を読み込んでください。";

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _detectCts?.Cancel();
        _batchCts?.Cancel();
        _videoCts?.Cancel();
        if (_videoExporting || _videoOpening)
        {
            e.Cancel = true;
            _vm.Status = "動画処理が終了してから閉じてください（停止を要求しました）。";
            return;
        }
        if (_isDetecting)
        {
            e.Cancel = true;
            _vm.Status = "推論が終了してから閉じてください（停止を要求しました）。";
            return;
        }
        StopLiveBridgeProcess();
        _detector?.Dispose();
        _settings.MaskStyle = CurrentMaskStyle();
        _settings.Strength = (int)StrengthSlider.Value;
        try { SettingsStore.Save(_settings); }
        catch (IOException) { /* The MVP may still exit if settings cannot be persisted. */ }
    }
}
