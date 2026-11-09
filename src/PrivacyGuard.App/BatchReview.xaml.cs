using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PrivacyGuard.App.Services;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

public partial class MainWindow
{
    private readonly ObservableCollection<BatchJob> _batchJobs = new();
    private string? _batchRoot;
    private CancellationTokenSource? _batchCts;
    private bool _batchRunning;
    private bool _batchRecursive;

    private void InitializeBatchTab() => BatchJobsList.ItemsSource = _batchJobs;

    private void BatchStatus(string message)
    {
        BatchStatusText.Text = message;
        _vm.Status = "一括: " + message;
    }

    private void BatchSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _isDetecting || _videoScanning || _videoTracking || _videoExporting || _batchComparing) return;
        var dialog = new OpenFolderDialog { Title = "一括解析するフォルダを選択", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        _batchRoot = Path.GetFullPath(dialog.FolderName);
        BatchFolderText.Text = _batchRoot;
        _batchJobs.Clear();
        _batchExportReceipts.Clear();
        BatchSetDefaultOutput();
        BatchProgress.Value = 0;
        BatchStatus("フォルダを選択しました。スキャンを押してください。元ファイルは変更しません。");
    }

    private async void BatchScan_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _isDetecting || _videoScanning || _videoTracking || _videoExporting || _batchComparing) return;
        if (_batchRoot is null) { BatchStatus("フォルダを選択してください。"); return; }
        _batchRunning = true;
        BatchScanButton.IsEnabled = false;
        BatchRunButton.IsEnabled = false;
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        try
        {
            string root = _batchRoot;
            bool recursive = BatchRecursiveCheck.IsChecked == true;
            BatchStatus("フォルダ内を列挙中（画像・動画の中身はまだ解析しません）…");
            var inventory = await Task.Run(() => BatchQueuePlanner.Scan(root, recursive, token), token);
            token.ThrowIfCancellationRequested();
            var saved = BatchQueueStore.Load(root);
            var previous = saved is { Recursive: var savedRecursive } && savedRecursive == recursive
                ? saved.Jobs : new List<BatchJob>();
            var jobs = BatchQueuePlanner.Reconcile(inventory, previous);
            _batchRecursive = recursive;
            _batchJobs.Clear();
            foreach (var job in jobs) _batchJobs.Add(job);
            BatchSaveQueue();
            ReloadBatchExportReceipts();
            BatchRefreshProgress();
            BatchStatus($"{jobs.Count:N0}ファイル登録。処理済み{jobs.Count(x => x.Status == BatchJobStatus.Completed):N0}件を再利用。" +
                " 読み込み済みモデルがなければ解析は開始できません。");
        }
        catch (OperationCanceledException) { BatchStatus("フォルダスキャンを中止しました。"); }
        catch (Exception ex) { BatchStatus("フォルダスキャン失敗: " + ex.Message); }
        finally
        {
            _batchRunning = false;
            BatchScanButton.IsEnabled = true;
            BatchRunButton.IsEnabled = true;
            _batchCts.Dispose();
            _batchCts = null;
        }
    }

    private void BatchSaveQueue()
    {
        if (_batchRoot is not null)
            BatchQueueStore.Save(_batchRoot, _batchRecursive, _batchJobs);
    }

    private void BatchRefreshProgress()
    {
        int completed = _batchJobs.Count(x => x.Status == BatchJobStatus.Completed);
        int failed = _batchJobs.Count(x => x.Status == BatchJobStatus.Failed);
        BatchProgress.Value = _batchJobs.Count == 0 ? 0 :
            (completed + failed) * 100d / _batchJobs.Count;
        BatchSummaryText.Text = $"合計 {_batchJobs.Count:N0} / 解析済 {completed:N0} / エラー {failed:N0} / " +
            $"待機 {_batchJobs.Count(x => x.Status == BatchJobStatus.Pending):N0} | 0件候補も安全保証ではありません";
    }

    private void BatchRetryFailed_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _batchComparing) return;
        for (int i = 0; i < _batchJobs.Count; i++)
        {
            if (_batchJobs[i].Status == BatchJobStatus.Failed)
                _batchJobs[i] = _batchJobs[i] with { Status = BatchJobStatus.Pending, Error = null };
        }
        try { BatchSaveQueue(); BatchRefreshProgress(); BatchStatus("エラー項目を再実行待ちに戻しました。"); }
        catch (Exception ex) { BatchStatus("保存エラー: " + ex.Message); }
    }

    private void BatchResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _isDetecting || _batchComparing) return;
        for (int i = 0; i < _batchJobs.Count; i++)
            _batchJobs[i] = _batchJobs[i] with
            { Status = BatchJobStatus.Pending, CandidateCount = 0, Error = null, ModelId = null };
        try { BatchSaveQueue(); BatchRefreshProgress(); BatchStatus("全件を再解析待ちに戻しました。以前の確認記録は消去していません。"); }
        catch (Exception ex) { BatchStatus("保存エラー: " + ex.Message); }
    }

    private async void BatchRun_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _isDetecting || _videoScanning || _videoTracking || _videoExporting || _videoOpening || _isLoading || _batchComparing) return;
        if (_batchRoot is null || _batchJobs.Count == 0)
        { BatchStatus("先にフォルダを選択し、スキャンしてください。"); return; }
        if (_detector is null)
        { BatchStatus("AIモデル未選択です。画像タブで互換ONNXモデル定義JSONを読み込んでください。"); return; }

        if (_batchRecursive != (BatchRecursiveCheck.IsChecked == true))
        { BatchStatus("サブフォルダ設定が変わりました。再スキャンしてください。"); return; }
        var detector = _detector;
        string modelKey = _loadedManifest is { } manifest
            ? manifest.ModelId + ":" + manifest.ModelSha256[..12] : detector.ModelId;
        if (_batchJobs.Any(x => x.Status == BatchJobStatus.Completed && x.ModelId != modelKey))
            BatchStatus("異なるモデルで解析済みの項目は再利用します。新モデルでの再解析は「全件再解析」を押してください。");
        _batchRunning = true;
        _isDetecting = true; // Prevent concurrent model disposal and image/video scans.
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;
        BatchScanButton.IsEnabled = false;
        BatchRunButton.IsEnabled = false;
        ImageTab.IsEnabled = false;
        VideoTab.IsEnabled = false;
        try
        {
            // Persist Running before work; recovery turns Running back into Pending.
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var job = _batchJobs[i];
                if (job.Status != BatchJobStatus.Pending) continue;
                _batchJobs[i] = job with { Status = BatchJobStatus.Running, Error = null, ModelId = modelKey };
                BatchSaveQueue();
                BatchStatus($"{i + 1}/{_batchJobs.Count} {Path.GetFileName(job.SourcePath)} を解析中…");
                try
                {
                    if (!BatchQueuePlanner.SignatureMatches(job))
                        throw new IOException("ファイルが変更されました。再スキャンしてください。");
                    int newCandidates = job.Kind == BatchMediaKind.Image
                        ? await BatchAnalyzeImageAsync(job, detector, modelKey, ct)
                        : await BatchAnalyzeVideoAsync(job, detector, modelKey, ct);
                    ct.ThrowIfCancellationRequested();
                    _batchJobs[i] = job with { Status = BatchJobStatus.Completed,
                        CandidateCount = newCandidates, Error = null, ModelId = modelKey };
                }
                catch (OperationCanceledException)
                {
                    _batchJobs[i] = job with { Status = BatchJobStatus.Pending, Error = null };
                    BatchSaveQueue();
                    throw;
                }
                catch (Exception ex)
                {
                    _batchJobs[i] = job with { Status = BatchJobStatus.Failed,
                        Error = ex.Message.Length > 150 ? ex.Message[..150] : ex.Message };
                }
                BatchSaveQueue();
                BatchRefreshProgress();
                await Task.Yield();
            }
            BatchStatus("待機ジョブの解析が終わりました。各ファイルを開いて目視確認してください。自動加工はしていません。");
        }
        catch (OperationCanceledException)
        {
            BatchStatus("解析を中止しました。未処理ジョブは再開できます。");
        }
        catch (Exception ex)
        {
            BatchStatus("一括処理の中断: " + ex.Message);
        }
        finally
        {
            _batchRunning = false;
            _isDetecting = false;
            ImageTab.IsEnabled = true;
            VideoTab.IsEnabled = true;
            BatchScanButton.IsEnabled = true;
            BatchRunButton.IsEnabled = true;
            BatchRefreshProgress();
            _batchCts.Dispose();
            _batchCts = null;
        }
    }

    private async Task<int> BatchAnalyzeImageAsync(BatchJob job, IExposureDetector detector,
        string modelKey, CancellationToken ct)
    {
        PixelBuffer pixels = await ImageCodec.ReadAsync(job.SourcePath, ct);
        ct.ThrowIfCancellationRequested();
        var predictions = await detector.DetectAsync(pixels, ct);
        ct.ThrowIfCancellationRequested();
        // Check signature before committing any findings; stale media must be rescanned.
        if (!BatchQueuePlanner.SignatureMatches(job))
            throw new IOException("解析中にファイルが変わりました。再スキャンしてください。");
        await using var stream = new FileStream(job.SourcePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.SequentialScan);
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        ct.ThrowIfCancellationRequested();
        if (!BatchQueuePlanner.SignatureMatches(job))
            throw new IOException("ハッシュ取得中にファイルが変わりました。");
        Guid sourceId = _history.GetOrCreateSource(job.SourcePath, digest, pixels.Width, pixels.Height);
        var existing = _history.GetDetections(sourceId);
        var added = new List<DetectionRecord>();
        foreach (var candidate in predictions)
        {
            var bounds = candidate.Bounds.Clamp();
            if (bounds.Width <= 0 || bounds.Height <= 0 ||
                !double.IsFinite(candidate.Score) || candidate.Score is < 0 or > 1) continue;
            string origin = "onnx:" + modelKey;
            if (existing.Concat(added).Any(x => x.Category == candidate.Category &&
                x.Origin == origin && ModelTensorProcessor.IntersectionOverUnion(x.Bounds, bounds) >= 0.8)) continue;
            added.Add(new DetectionRecord(Guid.NewGuid(), sourceId, candidate.Category,
                candidate.Score, bounds, ReviewDecision.Pending, origin, DateTimeOffset.UtcNow));
        }
        ct.ThrowIfCancellationRequested();
        _history.InsertDetections(added);
        return added.Count;
    }

    private async Task<int> BatchAnalyzeVideoAsync(BatchJob job, IExposureDetector detector,
        string modelKey, CancellationToken ct)
    {
        var metadata = await _videoService.ProbeAsync(job.SourcePath, ct);
        const int rate = 1; // R4.0 CPU-friendly, deliberately sparse and not safety-certifying.
        var observations = new List<TimedDetection>();
        await foreach (var sample in _videoService.StreamSamplesAsync(job.SourcePath, metadata, rate, ct))
        {
            ct.ThrowIfCancellationRequested();
            var predictions = await detector.DetectAsync(sample.Pixels, ct);
            foreach (var candidate in predictions)
            {
                var rect = candidate.Bounds.Clamp();
                if (rect.Width <= 0 || rect.Height <= 0 ||
                    !double.IsFinite(candidate.Score) || candidate.Score is < 0 or > 1) continue;
                observations.Add(new TimedDetection(sample.TimestampUs, candidate.Category,
                    candidate.Score, rect));
                if (observations.Count > 60_000) throw new NotSupportedException("検出件数が上限を超えました。");
            }
        }
        ct.ThrowIfCancellationRequested();
        if (!BatchQueuePlanner.SignatureMatches(job))
            throw new IOException("解析中に動画が変更されました。再スキャンしてください。");
        long strideUs = 1_000_000;
        var events = VideoEventGrouping.Group(observations, strideUs + strideUs / 3, minIou: 0.2);
        var existing = await Task.Run(() => VideoReviewStore.Load(job.SourcePath), ct);
        var additions = new List<VideoMaskCandidate>();
        foreach (var evt in events)
        {
            var best = evt.Detections.OrderByDescending(d => d.Score).First();
            long start = Math.Max(0, evt.StartUs - strideUs / 2);
            long end = Math.Min(metadata.DurationUs, evt.EndUs + strideUs / 2);
            if (end <= start) continue;
            var keys = evt.Detections.GroupBy(d => d.TimestampUs)
                .Select(g => g.OrderByDescending(d => d.Score).First())
                .OrderBy(d => d.TimestampUs)
                .Select(d => new VideoMaskKeyframe(d.TimestampUs, d.Bounds)).ToArray();
            var region = new VideoMaskCandidate(Guid.NewGuid(), start, end, best.Bounds,
                ReviewDecision.Pending, evt.Category, "onnx:sampled_1fps:" + modelKey,
                best.Score, keys);
            if (existing.Concat(additions).Any(x => x.Origin == region.Origin &&
                x.Category == region.Category && x.StartUs < region.EndUs && x.EndUs > region.StartUs &&
                ModelTensorProcessor.IntersectionOverUnion(x.Bounds, region.Bounds) > 0.65)) continue;
            VideoMaskPlan.Validate(region, metadata.DurationUs);
            additions.Add(region);
        }
        ct.ThrowIfCancellationRequested();
        await Task.Run(() => VideoReviewStore.Save(job.SourcePath,
            existing.Concat(additions).ToArray()), ct);
        return additions.Count;
    }

    private void BatchCancel_Click(object sender, RoutedEventArgs e)
    {
        _batchCts?.Cancel();
        BatchStatus("停止を要求しました。現在のファイルを中断します。");
    }

    private async void BatchOpenForReview_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _batchComparing || _isDetecting || _videoExporting || _videoOpening || _videoTracking || _videoScanning ||
            BatchJobsList.SelectedItem is not BatchJob job) return;
        try
        {
            if (!BatchQueuePlanner.SignatureMatches(job))
                throw new IOException("ファイルが変更されています。再スキャンしてください。");
            if (job.Kind == BatchMediaKind.Image)
            {
                MainTabs.SelectedItem = ImageTab;
                await OpenImagePathAsync(job.SourcePath);
            }
            else
            {
                MainTabs.SelectedItem = VideoTab;
                await VideoOpenPathAsync(job.SourcePath);
            }
        }
        catch (Exception ex) { BatchStatus("ファイルを開けません: " + ex.Message); }
    }
}
