using System.Collections.ObjectModel;
using System.IO;
using Path = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PrivacyGuard.App.Services;
using PrivacyGuard.Core;
using PrivacyGuard.Media;

namespace PrivacyGuard.App;

public partial class MainWindow
{
    private readonly FfmpegVideoService _videoService = new();
    private readonly ObservableCollection<VideoMaskCandidate> _videoCandidates = new();
    private VideoMetadata? _videoMetadata;
    private string? _videoPath;
    private PixelBuffer? _videoCurrentFrame;
    private long _videoCurrentUs;
    private long _videoMarkStartUs = -1;
    private long _videoMarkEndUs = -1;
    private NormalizedRect? _videoDraftRect;
    private Point? _videoDragStart;
    private CancellationTokenSource? _videoCts;
    private int _videoGeneration;
    private bool _videoExporting;
    private bool _videoOpening;
    private bool _videoScanning;
    private bool _videoIndexing;
    private bool _videoSuppressSelectionSeek;
    private VideoFrameIndex? _videoFrameIndex;
    private CancellationTokenSource? _videoIndexCts;
    private CancellationTokenSource? _videoTrackCts;
    private bool _videoTracking;
    private VideoBlockTracker.Proposal? _videoMotionProposal;
    private VideoMaskCandidate? _videoMotionSourceCandidate;

    private void InitializeVideoTab()
    {
        VideoCandidateList.ItemsSource = _videoCandidates;
        VideoZoom(1);
    }

    private void VideoStatus(string message)
    {
        VideoStatusText.Text = message;
        _vm.Status = "動画: " + message;
    }

    private async void VideoOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_videoExporting || _videoOpening || _isDetecting || _videoScanning || _videoIndexing || _videoTracking) return;
        var dialog = new OpenFileDialog
        {
            Filter = "動画|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|全ファイル|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await VideoOpenPathAsync(dialog.FileName);
    }

    private async Task VideoOpenPathAsync(string filePath)
    {
        if (_videoExporting || _videoOpening || _isDetecting || _videoScanning || _videoIndexing || _videoTracking) return;
        _videoOpening = true;
        _videoCts?.Cancel();
        _videoCts = new CancellationTokenSource();
        var ct = _videoCts.Token;
        try
        {
            VideoStatus("FFprobeで動画の情報を読み込み中…");
            string fullPath = Path.GetFullPath(filePath);
            var metadata = await _videoService.ProbeAsync(fullPath, ct);
            ct.ThrowIfCancellationRequested();
            _videoPath = fullPath;
            _videoMetadata = metadata;
            _videoFrameIndex = null;
            _videoMotionProposal = null;
            _videoMotionSourceCandidate = null;
            _videoCurrentFrame = null;
            VideoFrameImage.Source = null;
            VideoCropImage.Source = null;
            VideoTrackText.Text = "選択候補：なし。追従点を追加できます。";
            _videoCurrentUs = 0;
            _videoMarkStartUs = -1;
            _videoMarkEndUs = -1;
            _videoDraftRect = null;
            _videoCandidates.Clear();
            try
            {
                foreach (var candidate in await Task.Run(() => VideoReviewStore.Load(fullPath), ct))
                {
                    VideoMaskPlan.Validate(candidate, metadata.DurationUs);
                    _videoCandidates.Add(candidate);
                }
            }
            catch (Exception ex) { VideoStatus("レビュー情報を読み込めませんでした: " + ex.Message); }
            VideoTimelineSlider.Maximum = metadata.DurationUs / 1_000_000d;
            VideoTimelineSlider.Value = 0;
            VideoMetaText.Text = $"{Path.GetFileName(fullPath)} / {metadata.Summary} / " +
                $"保存済み候補 {_videoCandidates.Count}件";
            VideoRangeDisplay();
            VideoScanProgress.Value = 0;
            await VideoShowFrameAsync(0);
        }
        catch (OperationCanceledException) { VideoStatus("動画の読み込みを中止しました。"); }
        catch (Exception ex) { VideoStatus("動画読み込みに失敗: " + ex.Message); }
        finally { _videoOpening = false; }
    }

    private void VideoTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VideoTimeText is null || VideoTimelineSlider is null) return;
        double duration = _videoMetadata?.DurationUs / 1_000_000d ?? 0;
        VideoTimeText.Text = $"選択 {VideoTimelineSlider.Value:0.000}s / {duration:0.000}s";
    }

    private async Task VideoShowFrameAsync(long targetUs)
    {
        if (_videoPath is null || _videoMetadata is null || _videoExporting || _videoScanning) return;
        int generation = ++_videoGeneration;
        _videoCts?.Cancel();
        _videoCts = new CancellationTokenSource();
        var ct = _videoCts.Token;
        string path = _videoPath;
        var metadata = _videoMetadata;
        targetUs = Math.Clamp(targetUs, 0, Math.Max(0, metadata.DurationUs - 10_000));
        try
        {
            VideoStatus($"時刻 {targetUs / 1_000_000d:0.000}s 付近のフレームを抽出中…");
            byte[] png = await _videoService.ExtractPngAsync(path, targetUs, metadata.DurationUs, ct);
            ct.ThrowIfCancellationRequested();
            if (generation != _videoGeneration || path != _videoPath) return;
            var pixels = ImageCodec.ReadEncodedBytes(png);
            _videoCurrentFrame = pixels;
            // A draft region belongs to the old displayed image; never reuse it at another time.
            _videoDraftRect = null;
            _videoCurrentUs = targetUs;
            VideoFrameImage.Source = ImageCodec.ToBitmap(pixels);
            VideoTimelineSlider.Value = targetUs / 1_000_000d;
            VideoDrawOverlay();
            VideoUpdateCrop();
            VideoStatus($"時刻 {targetUs / 1_000_000d:0.000}s 付近を表示。" +
                (_videoFrameIndex is null ? "推定フレーム移動です。" : "フレームPTS索引を使用（表示は近傍時刻）。"));
        }
        catch (OperationCanceledException) { /* newer seek superseded this frame */ }
        catch (Exception ex) { if (generation == _videoGeneration) VideoStatus("フレーム抽出エラー: " + ex.Message); }
    }

    private async void VideoSeek_Click(object sender, RoutedEventArgs e) =>
        await VideoShowFrameAsync((long)Math.Round(VideoTimelineSlider.Value * 1_000_000));
    private async void VideoPreviousSecond_Click(object sender, RoutedEventArgs e) => await VideoStepAsync(-1_000_000);
    private async void VideoNextSecond_Click(object sender, RoutedEventArgs e) => await VideoStepAsync(1_000_000);
    private async void VideoPreviousFrame_Click(object sender, RoutedEventArgs e) =>
        await VideoStepFrameAsync(-1);
    private async void VideoNextFrame_Click(object sender, RoutedEventArgs e) =>
        await VideoStepFrameAsync(1);

    private async Task VideoStepFrameAsync(int direction)
    {
        if (_videoFrameIndex is { } frameIndex)
            await VideoShowFrameAsync(frameIndex.Adjacent(_videoCurrentUs, direction));
        else
            await VideoStepAsync(direction * VideoEstimatedFrameUs());
    }

    private async void VideoIndexFrames_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null || _videoTracking || _videoIndexing || _videoScanning || _videoOpening ||
            _videoExporting || _isDetecting) return;
        string source = _videoPath;
        _videoIndexing = true;
        _videoIndexCts?.Dispose();
        _videoIndexCts = new CancellationTokenSource();
        try
        {
            VideoStatus("FFprobeでフレームの実時刻(PTS)を索引化中… 数分かかる場合があります。");
            var index = await _videoService.IndexFrameTimestampsAsync(source, _videoIndexCts.Token);
            if (_videoPath != source) return;
            _videoFrameIndex = index;
            VideoStatus($"フレームPTS索引 {index.Count:N0}件。±1コマ操作に利用します（プレビュー抽出は近傍時刻）。");
        }
        catch (OperationCanceledException) { VideoStatus("フレーム索引を中止しました。推定fpsの移動に戻ります。"); }
        catch (Exception ex) { VideoStatus("フレーム索引失敗: " + ex.Message); }
        finally { _videoIndexing = false; }
    }

    private async void VideoScanAll_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null || _videoMetadata is null || _videoTracking || _videoScanning || _videoIndexing ||
            _videoOpening || _videoExporting || _isDetecting) return;
        if (_detector is null)
        {
            VideoStatus("全動画AI走査には互換ONNXモデルが必要です。画像タブからモデル定義JSONを読み込んでください。");
            return;
        }
        int rate = VideoScanRateCombo.SelectedIndex switch { 0 => 1, 2 => 4, _ => 2 };
        string source = _videoPath;
        var metadata = _videoMetadata;
        var detector = _detector;
        var sourceModel = detector.ModelId;
        var collected = new List<TimedDetection>();
        _videoScanning = true;
        _isDetecting = true;
        _detectCts = new CancellationTokenSource();
        var token = _detectCts.Token;
        LoadModelButton.IsEnabled = false;
        DetectButton.IsEnabled = false;
        ModelProbeButton.IsEnabled = false;
        VideoScanButton.IsEnabled = false;
        VideoScanProgress.Value = 0;
        int frames = 0;
        try
        {
            VideoStatus($"全動画のサンプル走査開始: {rate}fps / {sourceModel}。サンプル間は検査しません。");
            await foreach (var sample in _videoService.StreamSamplesAsync(source, metadata, rate, token))
            {
                token.ThrowIfCancellationRequested();
                var candidates = await detector.DetectAsync(sample.Pixels, token);
                token.ThrowIfCancellationRequested();
                foreach (var candidate in candidates)
                {
                    var rect = candidate.Bounds.Clamp();
                    if (rect.Width <= 0 || rect.Height <= 0 || !double.IsFinite(candidate.Score) ||
                        candidate.Score is < 0 or > 1) continue;
                    collected.Add(new TimedDetection(sample.TimestampUs, candidate.Category,
                        candidate.Score, rect));
                    if (collected.Count > 60_000)
                        throw new NotSupportedException("検出件数が60,000件を超えたため走査を中断しました。");
                }
                frames++;
                if (frames % 10 == 0)
                {
                    VideoScanProgress.Value = Math.Clamp(sample.TimestampUs * 100.0 / metadata.DurationUs, 0, 99);
                    VideoStatus($"動画走査: {VideoScanProgress.Value:0.0}% / {frames:N0}標本 / {collected.Count:N0}候補。検出は未校正です。");
                }
            }
            token.ThrowIfCancellationRequested();
            long strideUs = 1_000_000L / rate;
            var events = VideoEventGrouping.Group(collected, maxGapUs: strideUs + strideUs / 3, minIou: 0.20);
            var newCandidates = new List<VideoMaskCandidate>();
            foreach (var evt in events)
            {
                var representative = evt.Detections.OrderByDescending(d => d.Score).First();
                long start = Math.Max(0, evt.StartUs - strideUs / 2);
                long end = Math.Min(metadata.DurationUs, evt.EndUs + strideUs / 2);
                if (end <= start) continue;
                // Sample positions are tentative track keys, NOT verified object tracking.
                // Users can inspect and overwrite them; frames between samples are not analyzed.
                var keys = evt.Detections.GroupBy(d => d.TimestampUs)
                    .Select(g => g.OrderByDescending(d => d.Score).First())
                    .OrderBy(d => d.TimestampUs)
                    .Select(d => new VideoMaskKeyframe(d.TimestampUs, d.Bounds)).ToArray();
                var proposed = new VideoMaskCandidate(Guid.NewGuid(), start, end,
                    representative.Bounds, ReviewDecision.Pending, evt.Category,
                    $"onnx:sampled_{rate}fps:{sourceModel}", representative.Score, keys);
                if (_videoCandidates.Any(x => x.Origin == proposed.Origin &&
                    x.Category == proposed.Category && x.StartUs < proposed.EndUs && x.EndUs > proposed.StartUs &&
                    ModelTensorProcessor.IntersectionOverUnion(x.Bounds, proposed.Bounds) > 0.65)) continue;
                VideoMaskPlan.Validate(proposed, metadata.DurationUs);
                newCandidates.Add(proposed);
            }
            // Commit only after a complete, successful scan. Cancellation leaves existing reviews untouched.
            VideoReviewStore.Save(source, _videoCandidates.Concat(newCandidates).ToList());
            foreach (var candidate in newCandidates) _videoCandidates.Add(candidate);
            VideoScanProgress.Value = 100;
            VideoDrawOverlay();
            VideoStatus($"サンプル走査完了: {frames:N0}枚 / 生検出{collected.Count:N0}件 / 要確認{newCandidates.Count:N0}イベント。" +
                "未確認のまま書き出しません。サンプル間の露出・動きへの追従は保証されません。");
        }
        catch (OperationCanceledException)
        {
            VideoStatus("全動画走査を中止しました。未完了の検出は登録していません。");
        }
        catch (Exception ex)
        {
            VideoStatus("全動画走査失敗（結果未登録）: " + ex.Message);
        }
        finally
        {
            _videoScanning = false;
            _isDetecting = false;
            _detectCts?.Dispose();
            _detectCts = null;
            VideoScanButton.IsEnabled = true;
            LoadModelButton.IsEnabled = true;
            DetectButton.IsEnabled = true;
            ModelProbeButton.IsEnabled = _detector is not null;
        }
    }

    private long VideoEstimatedFrameUs() => _videoMetadata is { EstimatedFramesPerSecond: > 0 } m
        ? Math.Max(1, (long)Math.Round(1_000_000 / m.EstimatedFramesPerSecond)) : 33_333;

    private async Task VideoStepAsync(long delta)
    {
        if (_videoMetadata is null) return;
        await VideoShowFrameAsync(Math.Clamp(_videoCurrentUs + delta, 0, _videoMetadata.DurationUs));
    }

    private void VideoMarkStart_Click(object sender, RoutedEventArgs e)
    {
        if (_videoMetadata is null) return;
        _videoMarkStartUs = _videoCurrentUs;
        VideoRangeDisplay();
    }
    private void VideoMarkEnd_Click(object sender, RoutedEventArgs e)
    {
        if (_videoMetadata is null) return;
        _videoMarkEndUs = Math.Min(_videoMetadata.DurationUs, _videoCurrentUs + VideoEstimatedFrameUs());
        VideoRangeDisplay();
    }
    private void VideoRangeDisplay()
    {
        string start = _videoMarkStartUs < 0 ? "未設定" : $"{_videoMarkStartUs / 1_000_000d:0.000}s";
        string end = _videoMarkEndUs < 0 ? "未設定" : $"{_videoMarkEndUs / 1_000_000d:0.000}s";
        VideoRangeText.Text = $"加工区間: {start}–{end} | " +
            "開始・終了の指定＋矩形ドラッグで候補作成。追従点は候補選択後に追加可能。";
    }

    private (double s, double x, double y) VideoTransform()
    {
        if (_videoCurrentFrame is null || VideoFrameCanvas.ActualWidth < 1 || VideoFrameCanvas.ActualHeight < 1)
            return (0, 0, 0);
        double s = Math.Min(VideoFrameCanvas.ActualWidth / _videoCurrentFrame.Width,
            VideoFrameCanvas.ActualHeight / _videoCurrentFrame.Height);
        return (s, (VideoFrameCanvas.ActualWidth - s * _videoCurrentFrame.Width) / 2,
            (VideoFrameCanvas.ActualHeight - s * _videoCurrentFrame.Height) / 2);
    }
    private Point VideoNormalized(Point local)
    {
        var (s, x, y) = VideoTransform();
        if (_videoCurrentFrame is null || s == 0) return new Point();
        return new Point(
            Math.Clamp((local.X - x) / (s * _videoCurrentFrame.Width), 0, 1),
            Math.Clamp((local.Y - y) / (s * _videoCurrentFrame.Height), 0, 1));
    }
    private void VideoFrameCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_videoCurrentFrame is null || _videoExporting) return;
        _videoDragStart = VideoNormalized(e.GetPosition(VideoFrameCanvas));
        _videoDraftRect = null;
        VideoFrameCanvas.CaptureMouse();
        e.Handled = true;
    }
    private void VideoFrameCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_videoDragStart is not { } start) return;
        var pos = VideoNormalized(e.GetPosition(VideoFrameCanvas));
        _videoDraftRect = RegionEditor.FromPoints(start.X, start.Y, pos.X, pos.Y);
        VideoDrawOverlay();
    }
    private void VideoFrameCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_videoDragStart is null) return;
        VideoFrameCanvas_MouseMove(sender, e);
        _videoDragStart = null;
        if (VideoFrameCanvas.IsMouseCaptured) VideoFrameCanvas.ReleaseMouseCapture();
        VideoDrawOverlay();
        VideoStatus("範囲を選択しました。開始・終了時刻を確認して「選択範囲を候補登録」を押してください。");
    }
    private void VideoFrameCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => VideoDrawOverlay();
    private void VideoFrameCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _videoDragStart = null;
        VideoDrawOverlay();
    }

    private void VideoDrawOverlay()
    {
        if (VideoFrameCanvas is null) return;
        VideoFrameCanvas.Children.Clear();
        var (s, x, y) = VideoTransform();
        if (_videoCurrentFrame is null || s == 0) return;
        foreach (var c in _videoCandidates)
            if (_videoCurrentUs >= c.StartUs && _videoCurrentUs <= c.EndUs)
            {
                VideoDrawRect(c.BoundsAt(_videoCurrentUs), ReferenceEquals(c, VideoCandidateList.SelectedItem) ? Brushes.Orange : Brushes.DeepSkyBlue, s, x, y);
                // Purple is the UNSAVED suggested trajectory; orange is the currently saved mask.
                if (ReferenceEquals(c, _videoMotionSourceCandidate) &&
                    _videoMotionProposal is { Completed: true } proposal)
                    VideoDrawRect(VideoMotionTrack.BoundsAt(c with { Keyframes = proposal.Keys }, _videoCurrentUs),
                        Brushes.MediumPurple, s, x, y);
            }
        if (_videoDraftRect is { } bounds) VideoDrawRect(bounds, Brushes.LimeGreen, s, x, y);
    }
    private void VideoDrawRect(NormalizedRect bounds, Brush stroke, double scale, double offsetX, double offsetY)
    {
        if (_videoCurrentFrame is null) return;
        var r = bounds.Clamp();
        if (r.Width <= 0 || r.Height <= 0) return;
        var rectangle = new Rectangle
        {
            Width = r.Width * _videoCurrentFrame.Width * scale,
            Height = r.Height * _videoCurrentFrame.Height * scale,
            Stroke = stroke, StrokeThickness = 2, Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, offsetX + r.X * _videoCurrentFrame.Width * scale);
        Canvas.SetTop(rectangle, offsetY + r.Y * _videoCurrentFrame.Height * scale);
        VideoFrameCanvas.Children.Add(rectangle);
    }

    private void VideoAddCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking) { VideoStatus("追従提案中は候補を追加できません。"); return; }
        if (_videoCurrentFrame is null || _videoMetadata is null || _videoPath is null || _videoDraftRect is null) { VideoStatus("動画を開いて、映像上で矩形をドラッグしてください。"); return; }
        var bounds = _videoDraftRect.Value.Clamp();
        if (bounds.Width * _videoCurrentFrame.Width < 4 || bounds.Height * _videoCurrentFrame.Height < 4) { VideoStatus("範囲が小さすぎます。"); return; }
        if (_videoMarkStartUs < 0 || _videoMarkEndUs < 0 || _videoMarkStartUs >= _videoMarkEndUs)
        { VideoStatus("区間の開始・終了を両方設定し、開始が終了より前になるようにしてください。"); return; }
        var c = new VideoMaskCandidate(Guid.NewGuid(), _videoMarkStartUs, _videoMarkEndUs,
            bounds, ReviewDecision.Pending, "MANUAL_REGION", "manual");
        _videoCandidates.Add(c);
        _videoDraftRect = null;
        VideoCandidateList.SelectedItem = c;
        VideoSaveReviewSilently();
        VideoDrawOverlay();
        VideoUpdateCrop();
        VideoStatus("候補を登録しました。確認して「隠す」で承認してください。");
    }

    private async void VideoDetectFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking) { VideoStatus("追従提案中はAI解析できません。"); return; }
        if (_detector is null) { VideoStatus("AIモデルが読み込まれていません。画像タブでモデルを設定してください。"); return; }
        if (_videoCurrentFrame is null || _videoMetadata is null || _isDetecting) return;
        var frame = _videoCurrentFrame;
        long timestampUs = _videoCurrentUs;
        _isDetecting = true;
        _detectCts = new CancellationTokenSource();
        LoadModelButton.IsEnabled = false;
        DetectButton.IsEnabled = false;
        ModelProbeButton.IsEnabled = false;
        try
        {
            VideoStatus("現在表示中の1フレームだけをAI推論中…");
            var results = await _detector.DetectAsync(frame, _detectCts.Token);
            _detectCts.Token.ThrowIfCancellationRequested();
            long end = Math.Min(_videoMetadata.DurationUs, timestampUs + VideoEstimatedFrameUs());
            int added = 0;
            foreach (var result in results)
            {
                if (end <= timestampUs) break;
                var c = new VideoMaskCandidate(Guid.NewGuid(), timestampUs, end,
                    result.Bounds.Clamp(), ReviewDecision.Pending, result.Category,
                    "onnx:single_frame", result.Score);
                VideoMaskPlan.Validate(c, _videoMetadata.DurationUs);
                _videoCandidates.Add(c);
                added++;
            }
            VideoSaveReviewSilently();
            VideoDrawOverlay();
            VideoStatus($"単一フレームのAI候補 {added}件を追加しました。動画全体の検査・追従ではありません。");
        }
        catch (OperationCanceledException) { VideoStatus("動画フレームのAI解析を中止しました。"); }
        catch (Exception ex) { VideoStatus("動画AI解析に失敗: " + ex.Message); }
        finally
        {
            _isDetecting = false;
            _detectCts?.Dispose();
            _detectCts = null;
            LoadModelButton.IsEnabled = true;
            DetectButton.IsEnabled = true;
            ModelProbeButton.IsEnabled = _detector is not null;
        }
    }

    private async void VideoCandidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        VideoUpdateCrop();
        VideoDrawOverlay();
        VideoUpdateTrackText();
        if (!_videoSuppressSelectionSeek && VideoCandidateList.SelectedItem is VideoMaskCandidate candidate && _videoMetadata is not null)
            await VideoShowFrameAsync(candidate.StartUs);
    }
    private void VideoUpdateCrop()
    {
        if (_videoCurrentFrame is not { } frame || VideoCandidateList.SelectedItem is not VideoMaskCandidate c)
        {
            if (VideoCropImage is not null) VideoCropImage.Source = null;
            return;
        }
        var bounds = ReferenceEquals(c, _videoMotionSourceCandidate) &&
            _videoMotionProposal is { Completed: true } proposal
            ? VideoMotionTrack.BoundsAt(c with { Keyframes = proposal.Keys }, _videoCurrentUs)
            : c.BoundsAt(_videoCurrentUs);
        try { VideoCropImage.Source = ImageCodec.ToBitmap(frame.Crop(bounds, _settings.CropMargin)); }
        catch { VideoCropImage.Source = null; }
    }
    private void VideoUpdateTrackText()
    {
        if (VideoTrackText is null) return;
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate candidate)
        { VideoTrackText.Text = "選択候補：なし。追従点を追加できます。"; return; }
        if (ReferenceEquals(candidate, _videoMotionSourceCandidate) &&
            _videoMotionProposal is { Completed: true } proposal)
        {
            VideoTrackText.Text = $"紫＝未保存の自動追従案 {proposal.Keys.Count}点 / オレンジ＝保存済み。" +
                "タイムラインを移動して全区間を比較し、良ければ『提案を確認して適用』。";
            return;
        }
        int count = candidate.Keyframes?.Count ?? 0;
        VideoTrackText.Text = count < 2
            ? $"選択候補：固定範囲（追従点 {count}件）。フレームを移動し、矩形をドラッグ→追従点を追加。"
            : $"選択候補：追従点 {count}件。中間位置は線形補間。サンプル間の見逃しには注意。";
    }

    private void VideoReplaceSelected(VideoMaskCandidate original, VideoMaskCandidate replacement)
    {
        _videoMotionProposal = null;
        _videoMotionSourceCandidate = null;
        VideoTrackApplyButton.IsEnabled = false;
        int index = _videoCandidates.IndexOf(original);
        if (index < 0) return;
        _videoSuppressSelectionSeek = true;
        try { _videoCandidates[index] = replacement; VideoCandidateList.SelectedItem = replacement; }
        finally { _videoSuppressSelectionSeek = false; }
        VideoSaveReviewSilently();
        VideoDrawOverlay();
        VideoUpdateCrop();
        VideoUpdateTrackText();
    }

    private void VideoKeyframeAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking || _videoScanning || _videoExporting || _videoOpening || _videoIndexing)
        { VideoStatus("他の動画処理が完了してから追従点を編集してください。"); return; }
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate selected || _videoDraftRect is null)
        { VideoStatus("候補を選択し、映像上で新しい追従位置をドラッグしてください。"); return; }
        if (_videoCurrentUs < selected.StartUs || _videoCurrentUs > selected.EndUs)
        { VideoStatus("選択候補の加工区間内へ移動してください。"); return; }
        var newRect = _videoDraftRect.Value.Clamp();
        if (_videoCurrentFrame is null || newRect.Width * _videoCurrentFrame.Width < 4 ||
            newRect.Height * _videoCurrentFrame.Height < 4)
        { VideoStatus("追従範囲が小さすぎます。"); return; }
        var baseKeys = selected.Keyframes?.ToList() ?? new List<VideoMaskKeyframe>();
        if (baseKeys.Count == 0) baseKeys.Add(new VideoMaskKeyframe(selected.StartUs, selected.Bounds));
        var keys = VideoMotionTrack.Upsert(baseKeys, new VideoMaskKeyframe(_videoCurrentUs, newRect));
        var replacement = selected with { Keyframes = keys };
        if (_videoMetadata is { } meta) VideoMaskPlan.Validate(replacement, meta.DurationUs);
        _videoDraftRect = null;
        VideoReplaceSelected(selected, replacement);
        VideoStatus($"追従点を追加・更新しました: {_videoCurrentUs / 1_000_000d:0.000}s / {keys.Count}点。補間の動きを必ず確認してください。");
    }

    private void VideoKeyframeRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking) { VideoStatus("追跡提案処理中は追従点を変更できません。"); return; }
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate selected ||
            selected.Keyframes is not { Count: > 0 } keys)
        { VideoStatus("この候補に追従点はありません。"); return; }
        var nearest = keys.OrderBy(k => Math.Abs(k.TimestampUs - _videoCurrentUs)).First();
        // Only remove a keyframe near the displayed timestamp, not a distant accidental selection.
        if (Math.Abs(nearest.TimestampUs - _videoCurrentUs) > Math.Max(VideoEstimatedFrameUs(), 50_000))
        { VideoStatus("この時刻の近くに追従点がありません。"); return; }
        var remaining = keys.Where(k => k.TimestampUs != nearest.TimestampUs).ToArray();
        var replacement = selected with { Keyframes = remaining.Length == 0 ? null : remaining,
            Bounds = remaining.Length == 1 ? remaining[0].Bounds : selected.Bounds };
        VideoReplaceSelected(selected, replacement);
        VideoStatus($"追従点を削除しました。残り {remaining.Length}点。");
    }

    /// <summary>Experimental block-matching proposal; results NEVER modify saved masks until applied.</summary>
    private async void VideoTrackPropose_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking || _videoScanning || _videoExporting || _videoOpening ||
            _videoIndexing || _isDetecting || _videoPath is null || _videoMetadata is null)
        { VideoStatus("動画を読み込み、ほかの処理が終わってから実行してください。"); return; }
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate candidate)
        { VideoStatus("先に追跡する候補を選択してください。"); return; }
        if (candidate.EndUs - candidate.StartUs > 15_000_000 ||
            candidate.EndUs - candidate.StartUs < 300_000)
        { VideoStatus("R3.3は0.3～15秒の候補だけ追跡します。長い区間は手動で分割してください。"); return; }
        string source = _videoPath;
        var metadata = _videoMetadata;
        int rate = VideoTrackRateCombo.SelectedIndex == 0 ? 8 : 12;
        var initial = candidate.BoundsAt(candidate.StartUs);
        _videoMotionProposal = null;
        _videoMotionSourceCandidate = null;
        _videoTracking = true;
        _videoTrackCts?.Dispose();
        _videoTrackCts = new CancellationTokenSource();
        var ct = _videoTrackCts.Token;
        VideoTrackProposeButton.IsEnabled = false;
        VideoTrackApplyButton.IsEnabled = false;
        try
        {
            VideoStatus($"ブロックマッチングによる追従提案を計算中… {rate}fps（自動承認はしません）");
            async IAsyncEnumerable<(long TimestampUs, PixelBuffer Pixels)> Frames()
            {
                await foreach (var frame in _videoService.StreamTrackingSamplesAsync(source,
                    metadata, candidate.StartUs, candidate.EndUs, rate, ct))
                    yield return (frame.TimestampUs, frame.Pixels);
            }
            // CPU-heavy block matching stays off the WPF UI synchronization context.
            var proposed = await Task.Run(() => VideoBlockTracker.ProposeAsync(Frames(), initial,
                candidate.StartUs, candidate.EndUs, rate, ct), ct);
            ct.ThrowIfCancellationRequested();
            if (_videoPath != source || !ReferenceEquals(VideoCandidateList.SelectedItem, candidate))
            { VideoStatus("動画または選択候補が変更されたため、追跡結果を破棄しました。"); return; }
            _videoMotionProposal = proposed;
            _videoMotionSourceCandidate = candidate;
            VideoTrackApplyButton.IsEnabled = proposed.Completed;
            if (!proposed.Completed)
            {
                string when = proposed.BreakUs is { } us ? $"{us / 1_000_000d:0.000}s" : "時刻不明";
                VideoStatus($"追跡が途切れました: {when} / {proposed.Reason}。未完成提案は適用できません。" +
                    " 手動追従点で補正するか、区間を分割してください。");
            }
            else
            {
                VideoTrackApplyButton.IsEnabled = true;
                VideoDrawOverlay();
                VideoUpdateCrop();
                VideoUpdateTrackText();
                VideoStatus($"追従候補 {proposed.Keys.Count}点、平均一致度 {proposed.AverageQuality:0.00}。" +
                    " これは正解率ではありません。「提案を適用」後も全区間の目視確認が必要です。");
            }
        }
        catch (OperationCanceledException) { VideoStatus("追従提案を中止。元の判定と追従点は変更していません。"); }
        catch (Exception ex) { VideoStatus("追従提案エラー（既存データはそのまま）: " + ex.Message); }
        finally
        {
            _videoTracking = false;
            VideoTrackProposeButton.IsEnabled = true;
        }
    }

    private void VideoTrackApply_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking || _videoMotionProposal is not { Completed: true } proposal ||
            _videoMotionSourceCandidate is not { } original ||
            !ReferenceEquals(VideoCandidateList.SelectedItem, original))
        { VideoStatus("有効な追従提案がありません。候補を選択して提案を再生成してください。"); return; }
        if (MessageBox.Show(this,
                $"提案した追従点 {proposal.Keys.Count}点で既存の点を置き換えます。" +
                "判定は必ず『未確認』に戻ります。全フレームの確認が必要です。続けますか？",
                "追跡は正確さを保証しません", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var replacement = original with { Keyframes = proposal.Keys, Decision = ReviewDecision.Pending };
        if (_videoMetadata is { } meta) VideoMaskPlan.Validate(replacement, meta.DurationUs);
        VideoReplaceSelected(original, replacement);
        VideoTrackApplyButton.IsEnabled = false;
        VideoStatus($"提案 {proposal.Keys.Count}点を適用し、未確認に戻しました。書き出し前に全区間を必ず確認してください。");
    }

    private void VideoZoom(int zoom) => VideoCropImage.LayoutTransform = new ScaleTransform(zoom, zoom);
    private void VideoZoom1_Click(object sender, RoutedEventArgs e) => VideoZoom(1);
    private void VideoZoom2_Click(object sender, RoutedEventArgs e) => VideoZoom(2);
    private void VideoZoom4_Click(object sender, RoutedEventArgs e) => VideoZoom(4);
    private void VideoZoom8_Click(object sender, RoutedEventArgs e) => VideoZoom(8);

    private void VideoDecide(ReviewDecision decision)
    {
        if (_videoTracking) { VideoStatus("追従提案中は判定を変更できません。"); return; }
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate selected) { VideoStatus("候補を選択してください。"); return; }
        int index = _videoCandidates.IndexOf(selected);
        if (index < 0) return;
        _videoMotionProposal = null;
        _videoMotionSourceCandidate = null;
        VideoTrackApplyButton.IsEnabled = false;
        var replacement = selected with { Decision = decision };
        _videoCandidates[index] = replacement;
        _videoSuppressSelectionSeek = true;
        try { VideoCandidateList.SelectedItem = replacement; }
        finally { _videoSuppressSelectionSeek = false; }
        VideoSaveReviewSilently();
        VideoUpdateTrackText();
        VideoStatus($"判定：{replacement.Display}。レビュー情報を保存しました。");
    }
    private void VideoApprove_Click(object sender, RoutedEventArgs e) => VideoDecide(ReviewDecision.Approved);
    private void VideoReject_Click(object sender, RoutedEventArgs e) => VideoDecide(ReviewDecision.Rejected);
    private void VideoDefer_Click(object sender, RoutedEventArgs e) => VideoDecide(ReviewDecision.Deferred);
    private void VideoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_videoTracking) { VideoStatus("追従提案中は候補を削除できません。"); return; }
        if (VideoCandidateList.SelectedItem is not VideoMaskCandidate selected) return;
        _videoMotionProposal = null;
        _videoMotionSourceCandidate = null;
        VideoTrackApplyButton.IsEnabled = false;
        _videoCandidates.Remove(selected);
        VideoSaveReviewSilently();
        VideoUpdateCrop();
        VideoDrawOverlay();
        VideoStatus("候補を削除しました。");
    }
    private void VideoSaveReviewSilently()
    {
        if (_videoPath is null) return;
        try { VideoReviewStore.Save(_videoPath, _videoCandidates); }
        catch (Exception ex) { VideoStatus("レビュー保存に失敗: " + ex.Message); }
    }
    private void VideoSaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null) { VideoStatus("先に動画を読み込んでください。"); return; }
        try { VideoReviewStore.Save(_videoPath, _videoCandidates); VideoStatus("判定と範囲をローカルのJSONに保存しました（画像は保存しません）。"); }
        catch (Exception ex) { VideoStatus("レビュー保存に失敗: " + ex.Message); }
    }

    private async void VideoExport_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null || _videoMetadata is null || _videoExporting || _videoOpening || _videoScanning || _videoIndexing || _videoTracking) return;
        var approved = _videoCandidates.Where(x => x.Decision == ReviewDecision.Approved).ToList();
        if (approved.Count == 0) { VideoStatus("承認済み候補がありません。候補を選び「隠す」を押してください。"); return; }
        var message = "R3.3の黒塗りはキーフレーム間を補間する試作版です。" +
            "AIによる動きの正確な追跡やサンプル間の露出検出は保証されません。" +
            "追従点が1個以下の候補は固定位置で加工します。書き出し後は必ず動画全体を確認してください。続けますか？";
        if (MessageBox.Show(this, message, "試作版の制限", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog
        {
            Filter = "MP4動画|*.mp4", DefaultExt = ".mp4", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_videoPath) + "_guarded.mp4",
            InitialDirectory = Path.GetDirectoryName(_videoPath), OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        string target = Path.GetFullPath(dialog.FileName);
        if (string.Equals(target, _videoPath, StringComparison.OrdinalIgnoreCase))
        { VideoStatus("元動画の上書きは禁止です。"); return; }
        _videoCts?.Cancel();
        _videoCts = new CancellationTokenSource();
        _videoExporting = true;
        try
        {
            VideoStatus($"黒塗りMP4を出力中… 承認 {approved.Count}件 / 追従 {approved.Count(x => x.IsMoving)}件（FFmpeg）");
            await _videoService.ExportBlackMasksAsync(_videoPath, target, _videoMetadata,
                approved, _videoCts.Token);
            VideoStatus($"書き出し完了: {target}。元動画は未変更。追従位置と全区間を必ず確認してください。");
        }
        catch (OperationCanceledException) { VideoStatus("動画書き出しを中止しました。部分出力は削除されます。"); }
        catch (Exception ex) { VideoStatus("動画書き出し失敗: " + ex.Message); }
        finally { _videoExporting = false; }
    }
    private void VideoCancel_Click(object sender, RoutedEventArgs e)
    {
        _videoCts?.Cancel();
        _videoIndexCts?.Cancel();
        _videoTrackCts?.Cancel();
        if (_isDetecting) _detectCts?.Cancel();
        VideoStatus("実行中の動画処理に中止を要求しました。");
    }
}
