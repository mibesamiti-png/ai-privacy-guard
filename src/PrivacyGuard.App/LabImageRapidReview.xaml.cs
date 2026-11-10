using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PrivacyGuard.App.Services;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

public partial class MainWindow
{
    private sealed record LabQuickImageRow(Guid Id, string Display, BitmapSource? Thumbnail);
    private sealed record LabQuickCandidateRow(Guid Id, string Display, DetectionRecord Detection);
    private sealed record LabQuickTruthRow(Guid Id, string Display);

    private readonly Dictionary<Guid, BitmapSource?> _labQuickThumbCache = [];
    private LabSource? _labQuickSource;
    private PixelBuffer? _labQuickPixels;
    private Guid _labQuickHistorySourceId;
    private int _labQuickLoadGeneration;
    private bool _labQuickRefreshingQueue;
    private bool _labQuickMissArmed;
    private bool _labQuickDragActive;
    private Point _labQuickDragStart;
    private NormalizedRect? _labQuickPreviewBox;

    private void LabQuickTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_labStore is null)
        {
            try
            {
                string local = System.IO.Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "AIPrivacyGuard", "validation_lab.json");
                _labStore = ValidationLabStore.Open(local);
            }
            catch (Exception ex)
            {
                LabQuickMessage("プロジェクトを開けません。元のファイルとバックアップを確認してください: " + ex.Message);
                return;
            }
        }
        LabQuickRefreshQueue();
    }

    private void LabReviewImage_Click(object sender, RoutedEventArgs e)
    {
        var selected = LabSelectedSource();
        if (selected is { Kind: LabMediaKind.Video })
        {
            LabSetStatus("動画は次のR8.3-Cでタイムライン検証を追加します。");
            return;
        }
        LabQuickReviewTab.IsSelected = true;
        LabQuickRefreshQueue();
        if (selected is not null) LabQuickSelectSource(selected.Id);
    }

    private static BitmapSource? LabQuickCreateThumbnail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private void LabQuickRefreshQueue()
    {
        if (_labStore is null || LabQuickImagesList is null || LabQuickQueueFilter is null) return;
        var previousId = (LabQuickImagesList.SelectedItem as LabQuickImageRow)?.Id
                         ?? _labQuickSource?.Id;
        var imageSources = _labStore.Sources.Where(s => s.Kind == LabMediaKind.Image).ToList();
        IEnumerable<LabSource> filtered = LabQuickQueueFilter.SelectedIndex switch
        {
            1 => imageSources.Where(s => s.ReviewState is not LabReviewState.FullyReviewed
                and not LabReviewState.Excluded),
            2 => imageSources.Where(s => s.ReviewState == LabReviewState.FullyReviewed),
            3 => imageSources.Where(s => s.Truth == LabTruthState.Positive),
            4 => imageSources.Where(s => s.Truth == LabTruthState.Uncertain),
            _ => imageSources
        };
        var list = filtered.ToList();
        var rows = new List<LabQuickImageRow>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var source = list[i];
            if (!_labQuickThumbCache.ContainsKey(source.Id) && _labQuickThumbCache.Count < 160)
                _labQuickThumbCache[source.Id] = LabQuickCreateThumbnail(source.Path);
            _labQuickThumbCache.TryGetValue(source.Id, out var thumbnail);
            var state = source.ReviewState == LabReviewState.FullyReviewed ? "✓ 確認完了" :
                source.Truth == LabTruthState.Positive ? "◉ 正解あり（未完了）" :
                source.Truth == LabTruthState.Uncertain ? "? 判断保留" : "… 未完了";
            rows.Add(new LabQuickImageRow(source.Id,
                $"{System.IO.Path.GetFileName(source.Path)}\n{state} / {source.Domain}", thumbnail));
        }
        _labQuickRefreshingQueue = true;
        try
        {
            LabQuickImagesList.ItemsSource = rows;
            LabQuickImagesList.SelectedItem = rows.FirstOrDefault(r => r.Id == previousId);
        }
        finally { _labQuickRefreshingQueue = false; }
        LabQuickQueueCount.Text =
            $"画像 {imageSources.Count}件 / 表示 {rows.Count}件 / 完了 {imageSources.Count(s => s.ReviewState == LabReviewState.FullyReviewed)}件" +
            "\nサムネイルは最大160件を軽量表示します。";

        if (LabQuickReviewTab.IsSelected && _labQuickPixels is null &&
            LabQuickImagesList.SelectedItem is LabQuickImageRow selected)
            _ = LabQuickLoadImageAsync(selected.Id);
    }

    private void LabQuickFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LabQuickRefreshQueue();

    private void LabQuickRefresh_Click(object sender, RoutedEventArgs e) =>
        LabQuickRefreshQueue();

    private void LabQuickImagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_labQuickRefreshingQueue || LabQuickImagesList.SelectedItem is not LabQuickImageRow row)
            return;
        _ = LabQuickLoadImageAsync(row.Id);
    }

    private void LabQuickSelectSource(Guid id)
    {
        var row = (LabQuickImagesList.ItemsSource as IEnumerable<LabQuickImageRow>)?
            .FirstOrDefault(x => x.Id == id);
        if (row is null)
        {
            LabQuickQueueFilter.SelectedIndex = 0;
            LabQuickRefreshQueue();
            row = (LabQuickImagesList.ItemsSource as IEnumerable<LabQuickImageRow>)?
                .FirstOrDefault(x => x.Id == id);
        }
        if (row is not null)
        {
            LabQuickImagesList.SelectedItem = row;
            LabQuickImagesList.ScrollIntoView(row);
            if (_labQuickSource?.Id != id && !(_isDetecting || _isLoading))
                _ = LabQuickLoadImageAsync(id);
        }
    }

    private async Task LabQuickLoadImageAsync(Guid id)
    {
        if (_labStore is null) return;
        if (_isDetecting || _isLoading)
        {
            LabQuickMessage("AI推論や画像読み込み中です。停止・完了してから画像を切り替えてください。");
            return;
        }
        int generation = ++_labQuickLoadGeneration;
        var source = _labStore.Sources.FirstOrDefault(s => s.Id == id);
        if (source is not { Kind: LabMediaKind.Image }) return;
        _labQuickPixels = null;
        _labQuickSource = null;
        _labQuickMissArmed = false;
        _labQuickPreviewBox = null;
        LabQuickImage.Source = null;
        LabQuickCanvas.Children.Clear();
        LabQuickProposalsList.ItemsSource = null;
        LabQuickTruthRegionsList.ItemsSource = null;
        LabQuickMessage("画像を開いてハッシュを検証中…");
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(id))
                throw new InvalidDataException("元ファイルが変更・削除されています。以前の正解データは再利用できません。");
            var pixels = await ImageCodec.ReadAsync(source.Path);
            if (!await _labStore.IsSourceCurrentAsync(id))
                throw new InvalidDataException("読み込み中に元画像が変更されました。");
            if (generation != _labQuickLoadGeneration) return;
            _labQuickSource = source;
            _labQuickPixels = pixels;
            _labQuickHistorySourceId = _history.GetOrCreateSource(source.Path,
                source.Sha256, pixels.Width, pixels.Height);
            LabQuickImage.Source = ImageCodec.ToBitmap(pixels);
            LabQuickRefreshAnnotations();
            LabQuickMessage($"読み込み完了 {pixels.Width}×{pixels.Height} / {source.Domain}。検出枠を確認してください。");
        }
        catch (Exception ex)
        {
            if (generation != _labQuickLoadGeneration) return;
            _labQuickPixels = null;
            _labQuickSource = null;
            LabQuickImage.Source = null;
            LabQuickMessage("画像を開けません: " + ex.Message);
        }
    }

    private void LabQuickRefreshAnnotations()
    {
        if (_labStore is null || _labQuickSource is null) return;
        var source = _labStore.Sources.FirstOrDefault(s => s.Id == _labQuickSource.Id);
        if (source is null) return;
        _labQuickSource = source;
        var previous = (LabQuickProposalsList.SelectedItem as LabQuickCandidateRow)?.Id;
        var reviewed = _labStore.Assessments.Where(a => a.SourceId == source.Id)
            .ToDictionary(a => a.CandidateId);
        var candidates = _history.GetDetections(_labQuickHistorySourceId)
            .Where(d => d.Origin.StartsWith("onnx:", StringComparison.Ordinal))
            .Select(d =>
            {
                string state = reviewed.TryGetValue(d.Id, out var result)
                    ? result.Verdict switch
                    {
                        LabProposalVerdict.Correct => "✓正解",
                        LabProposalVerdict.FalsePositive => "×誤検出",
                        _ => "?保留"
                    } : "未判定";
                return new LabQuickCandidateRow(d.Id,
                    $"{state}　{d.Category}\n{d.Origin}\nスコア {d.Confidence?.ToString("0.000") ?? "不明"}（未校正）",
                    d);
            }).ToArray();
        LabQuickProposalsList.ItemsSource = candidates;
        LabQuickProposalsList.SelectedItem = candidates.FirstOrDefault(x => x.Id == previous);
        var regions = _labStore.Regions.Where(r => r.SourceId == source.Id).ToArray();
        LabQuickTruthRegionsList.ItemsSource = regions.Select(r => new LabQuickTruthRow(r.Id,
            $"{r.Category}\nX {r.Bounds.X:P0}, Y {r.Bounds.Y:P0}; W {r.Bounds.Width:P0}, H {r.Bounds.Height:P0}"))
            .ToArray();
        LabQuickCountsText.Text = $"モデル候補 {candidates.Length}件 / 判定済 {reviewed.Count}件 / 人が追加・確認した正解枠 {regions.Length}件" +
            "\n未検出領域は「見逃し枠を描く」で登録。元動画の未確認時間は陰性扱いしません。";
        LabQuickRedrawOverlay();
    }

    private void LabQuickProposalsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LabQuickRedrawOverlay();

    private (double Scale, double Left, double Top) LabQuickImageTransform()
    {
        if (_labQuickPixels is null || LabQuickCanvas.ActualWidth <= 0 ||
            LabQuickCanvas.ActualHeight <= 0)
            return (0, 0, 0);
        var scale = Math.Min(LabQuickCanvas.ActualWidth / _labQuickPixels.Width,
            LabQuickCanvas.ActualHeight / _labQuickPixels.Height);
        return (scale,
            (LabQuickCanvas.ActualWidth - scale * _labQuickPixels.Width) / 2,
            (LabQuickCanvas.ActualHeight - scale * _labQuickPixels.Height) / 2);
    }

    private Point LabQuickToNormalized(Point p)
    {
        var (scale, left, top) = LabQuickImageTransform();
        if (_labQuickPixels is null || scale <= 0) return new Point(0, 0);
        return new Point(
            Math.Clamp((p.X - left) / (scale * _labQuickPixels.Width), 0, 1),
            Math.Clamp((p.Y - top) / (scale * _labQuickPixels.Height), 0, 1));
    }

    private void LabQuickDrawRect(NormalizedRect bounds, Brush stroke, double thickness)
    {
        var (scale, left, top) = LabQuickImageTransform();
        if (_labQuickPixels is null || scale <= 0) return;
        var rect = new Rectangle
        {
            Width = bounds.Width * _labQuickPixels.Width * scale,
            Height = bounds.Height * _labQuickPixels.Height * scale,
            Stroke = stroke, StrokeThickness = thickness,
            Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, left + bounds.X * _labQuickPixels.Width * scale);
        Canvas.SetTop(rect, top + bounds.Y * _labQuickPixels.Height * scale);
        LabQuickCanvas.Children.Add(rect);
    }

    private void LabQuickRedrawOverlay()
    {
        if (LabQuickCanvas is null) return;
        LabQuickCanvas.Children.Clear();
        if (_labQuickSource is null || _labStore is null || _labQuickPixels is null) return;
        var selectedCandidate = (LabQuickProposalsList.SelectedItem as LabQuickCandidateRow)?.Id;
        foreach (var candidate in (LabQuickProposalsList.ItemsSource as IEnumerable<LabQuickCandidateRow>
                     ?? []).Take(120))
            LabQuickDrawRect(candidate.Detection.Bounds,
                candidate.Id == selectedCandidate ? Brushes.Orange : Brushes.DeepSkyBlue,
                candidate.Id == selectedCandidate ? 2.6 : 1.3);
        foreach (var region in _labStore.Regions.Where(r => r.SourceId == _labQuickSource.Id).Take(120))
            LabQuickDrawRect(region.Bounds, Brushes.LimeGreen, 2.2);
        if (_labQuickPreviewBox is { } preview)
            LabQuickDrawRect(preview, Brushes.YellowGreen, 2.2);
    }

    private void LabQuickCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        LabQuickRedrawOverlay();

    private void LabQuickMiss_Click(object sender, RoutedEventArgs e)
    {
        if (_labQuickPixels is null || _labQuickSource is null)
        {
            LabQuickMessage("画像を選んでください。");
            return;
        }
        _labQuickMissArmed = true;
        LabQuickCanvas.Cursor = Cursors.Cross;
        LabQuickMessage("画像の見逃し領域を左上から右下へドラッグしてください。人が選んだラベルを保存します。");
    }

    private void LabQuickCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_labQuickMissArmed || _labQuickPixels is null || e.ChangedButton != MouseButton.Left)
            return;
        _labQuickDragActive = true;
        _labQuickDragStart = LabQuickToNormalized(e.GetPosition(LabQuickCanvas));
        _labQuickPreviewBox = null;
        LabQuickCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void LabQuickCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_labQuickDragActive) return;
        var now = LabQuickToNormalized(e.GetPosition(LabQuickCanvas));
        _labQuickPreviewBox = RegionEditor.FromPoints(_labQuickDragStart.X, _labQuickDragStart.Y,
            now.X, now.Y);
        LabQuickRedrawOverlay();
    }

    private async void LabQuickCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_labQuickDragActive) return;
        LabQuickCanvas_MouseMove(sender, e);
        _labQuickDragActive = false;
        _labQuickMissArmed = false;
        LabQuickCanvas.Cursor = Cursors.Arrow;
        if (LabQuickCanvas.IsMouseCaptured) LabQuickCanvas.ReleaseMouseCapture();
        var rect = _labQuickPreviewBox;
        _labQuickPreviewBox = null;
        if (_labStore is null || _labQuickSource is null || _labQuickPixels is null ||
            rect is null || rect.Value.Width * _labQuickPixels.Width < 4 ||
            rect.Value.Height * _labQuickPixels.Height < 4)
        {
            LabQuickMessage("小さすぎる枠、または無効な画像です。もう一度追加してください。");
            LabQuickRedrawOverlay();
            return;
        }
        var id = _labQuickSource.Id;
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(id))
                throw new InvalidDataException("元画像が変更されたため正解枠を保存しません。");
            if (_labQuickSource?.Id != id) return;
            _labStore.AddPositiveRegion(id, rect.Value, LabQuickSelectedTruthCategory());
            LabQuickRefreshAnnotations();
            LabQuickRefreshQueue();
            LabQuickMessage("見逃し候補の正解枠を保存しました。全体を確認後に『画像全体を確認完了』を押してください。");
        }
        catch (Exception ex) { LabQuickMessage("正解枠の保存に失敗: " + ex.Message); }
    }

    private void LabQuickCanvas_LostCapture(object sender, MouseEventArgs e)
    {
        if (!_labQuickDragActive) return;
        _labQuickDragActive = false;
        _labQuickMissArmed = false;
        _labQuickPreviewBox = null;
        LabQuickCanvas.Cursor = Cursors.Arrow;
        LabQuickRedrawOverlay();
    }

    private string LabQuickSelectedTruthCategory() =>
        (LabQuickTruthCategory.SelectedItem as ComboBoxItem)?.Tag as string
        ?? "other_reviewed_region";

    private async Task LabQuickAssessAsync(LabProposalVerdict verdict)
    {
        if (_labStore is null || _labQuickSource is null ||
            LabQuickProposalsList.SelectedItem is not LabQuickCandidateRow row)
        {
            LabQuickMessage("判定したいAI候補を選択してください。");
            return;
        }
        var sourceId = _labQuickSource.Id;
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(sourceId))
                throw new InvalidDataException("元画像が変更されています。検証データを保存できません。");
            if (_labQuickSource?.Id != sourceId) return;
            var d = row.Detection;
            _labStore.AssessProposal(sourceId, d.Id, d.Origin, d.Category, d.Bounds, verdict,
                verdict == LabProposalVerdict.Correct ? LabQuickSelectedTruthCategory() : null);
            LabQuickRefreshAnnotations();
            LabQuickRefreshQueue();
            LabQuickMessage(verdict switch
            {
                LabProposalVerdict.Correct => "AI候補を人が正解と確認し、同じ範囲に正解データを保存しました。",
                LabProposalVerdict.FalsePositive => "誤検出として保存。画像全体が陰性と判明したわけではありません。",
                _ => "このAI候補は判断保留です。"
            });
        }
        catch (Exception ex) { LabQuickMessage("AI候補の判定に失敗: " + ex.Message); }
    }

    private async void LabQuickCorrect_Click(object sender, RoutedEventArgs e) =>
        await LabQuickAssessAsync(LabProposalVerdict.Correct);
    private async void LabQuickFalsePositive_Click(object sender, RoutedEventArgs e) =>
        await LabQuickAssessAsync(LabProposalVerdict.FalsePositive);
    private async void LabQuickUncertain_Click(object sender, RoutedEventArgs e) =>
        await LabQuickAssessAsync(LabProposalVerdict.Uncertain);

    private async void LabQuickNegative_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null || _labQuickSource is null) return;
        var id = _labQuickSource.Id;
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(id))
                throw new InvalidDataException("元画像が変更されています。");
            if (_labQuickSource?.Id != id) return;
            var answer = MessageBox.Show(this,
                "画像全体を目視で確認し、対象領域が一つもないことを確認しましたか？\n"
                + "AIが検出しなかった範囲も目視確認が必要です。",
                "画像の全体陰性を確認", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            _labStore.MarkImageNegative(id, explicitlyInspected: true);
            LabQuickRefreshAnnotations();
            LabQuickRefreshQueue();
            LabQuickMessage("全体を確認した陰性画像として保存しました。");
        }
        catch (Exception ex) { LabQuickMessage("陰性記録に失敗: " + ex.Message); }
    }

    private async void LabQuickComplete_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null || _labQuickSource is null) return;
        var id = _labQuickSource.Id;
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(id))
                throw new InvalidDataException("元画像が変更されています。");
            if (_labQuickSource?.Id != id) return;
            var answer = MessageBox.Show(this,
                "画像全体（AIの検出がなかった領域も含む）と、表示されている各候補を十分に確認しましたか？\n"
                + "正解領域のある画像だけ『確認完了』にできます。",
                "画像全体の検証完了", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            _labStore.CompletePositiveImage(id, explicitlyInspected: true);
            LabQuickRefreshAnnotations();
            LabQuickRefreshQueue();
            LabQuickMessage("画像全体の目視確認済みとして保存しました。");
        }
        catch (Exception ex) { LabQuickMessage("確認完了にできません: " + ex.Message); }
    }

    private void LabQuickUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null || _labQuickSource is null) return;
        try
        {
            if (_labStore.NextUndoSourceId != _labQuickSource.Id)
            {
                LabQuickMessage("最後に編集した別の画像を開いてから取り消してください。");
                return;
            }
            if (!_labStore.UndoLastSourceEdit())
            {
                LabQuickMessage("元に戻す編集はありません。");
                return;
            }
            LabQuickRefreshAnnotations();
            LabQuickRefreshQueue();
            LabQuickMessage("直前の判定と紐付く正解枠を元に戻して保存しました。");
        }
        catch (Exception ex) { LabQuickMessage("取り消しに失敗: " + ex.Message); }
    }

    private void LabQuickNext_Click(object sender, RoutedEventArgs e) => LabQuickGo(+1, true);
    private void LabQuickPrevious_Click(object sender, RoutedEventArgs e) => LabQuickGo(-1, false);

    private void LabQuickGo(int direction, bool pendingOnly)
    {
        if (_labStore is null) return;
        var images = _labStore.Sources.Where(s => s.Kind == LabMediaKind.Image).ToList();
        if (images.Count == 0) { LabQuickMessage("登録された画像がありません。"); return; }
        int current = images.FindIndex(s => s.Id == _labQuickSource?.Id);
        if (current < 0) current = 0;
        for (int i = 1; i <= images.Count; i++)
        {
            int index = ((current + direction * i) % images.Count + images.Count) % images.Count;
            var source = images[index];
            if (pendingOnly && source.ReviewState is LabReviewState.FullyReviewed
                or LabReviewState.Excluded)
                continue;
            LabQuickSelectSource(source.Id);
            return;
        }
        LabQuickMessage("未完了の画像はありません。");
    }

    private void LabQuickPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control && e.Key == Key.Z)
        {
            LabQuickUndo_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (control) return;
        switch (e.Key)
        {
            case Key.D1 or Key.NumPad1: LabQuickCorrect_Click(sender, new RoutedEventArgs()); break;
            case Key.D2 or Key.NumPad2: LabQuickFalsePositive_Click(sender, new RoutedEventArgs()); break;
            case Key.D3 or Key.NumPad3 or Key.M: LabQuickMiss_Click(sender, new RoutedEventArgs()); break;
            case Key.D4 or Key.NumPad4: LabQuickUncertain_Click(sender, new RoutedEventArgs()); break;
            case Key.D5 or Key.NumPad5: LabQuickNegative_Click(sender, new RoutedEventArgs()); break;
            case Key.N: LabQuickNext_Click(sender, new RoutedEventArgs()); break;
            case Key.P: LabQuickPrevious_Click(sender, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    private async void LabQuickDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_labQuickPixels is null || _labQuickSource is null) return;
        if (_isDetecting || _isLoading || _videoScanning || _batchRunning || _detector is null)
        {
            LabQuickMessage("AIモデルが未選択、または他の処理が進行中です。画像タブでモデルを選択してください。");
            return;
        }
        var detector = _detector;
        var sourceId = _labQuickSource.Id;
        var pixels = _labQuickPixels;
        var historyId = _labQuickHistorySourceId;
        _detectCts = new CancellationTokenSource();
        _isDetecting = true;
        DetectButton.IsEnabled = false;
        LoadModelButton.IsEnabled = false;
        LabQuickMessage("ローカル画像をAI解析中… 結果は候補としてのみ扱います。");
        try
        {
            var predictions = await detector.DetectAsync(pixels, _detectCts.Token);
            _detectCts.Token.ThrowIfCancellationRequested();
            if (_labStore is null || _labQuickSource?.Id != sourceId ||
                !ReferenceEquals(_labQuickPixels, pixels) ||
                !await _labStore.IsSourceCurrentAsync(sourceId))
                throw new OperationCanceledException("元画像が変わったため結果を追加しません。");
            var current = _history.GetDetections(historyId).ToList();
            var added = new List<DetectionRecord>();
            foreach (var candidate in predictions.Take(250))
            {
                if (current.Concat(added).Any(d => d.Category == candidate.Category &&
                    d.Origin == "onnx:" + detector.ModelId &&
                    ModelTensorProcessor.IntersectionOverUnion(d.Bounds, candidate.Bounds) >= .8))
                    continue;
                added.Add(new DetectionRecord(Guid.NewGuid(), historyId, candidate.Category,
                    candidate.Score, candidate.Bounds, ReviewDecision.Pending,
                    "onnx:" + detector.ModelId, DateTimeOffset.UtcNow));
            }
            _history.InsertDetections(added);
            LabQuickRefreshAnnotations();
            LabQuickMessage($"AI候補を {added.Count}件追加しました。枠ごとに人が正誤判定してください。");
        }
        catch (OperationCanceledException) { LabQuickMessage("AI解析を停止・破棄しました。"); }
        catch (Exception ex) { LabQuickMessage("AI解析エラー: " + ex.Message); }
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

    private void LabQuickCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isDetecting) _detectCts?.Cancel();
    }

    private void LabQuickMessage(string message)
    {
        if (LabQuickCurrentStatus is not null)
            LabQuickCurrentStatus.Text = message;
    }
}
