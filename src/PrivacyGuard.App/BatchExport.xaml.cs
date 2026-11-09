using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using PrivacyGuard.App.Services;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

/// <summary>
/// User-approved batch export only. Pending/deferred findings block output; files with no
/// approved masks do not produce an unprocessed copy. No source media is ever overwritten.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<BatchExportReceipt> _batchExportReceipts = new();
    private string? _batchOutputRoot;
    private bool _batchComparing;
    private sealed record BatchCompareEvidence(string SourcePath, string SourceHash,
        string ReviewHash, string OutputHash, long TimestampUs);
    private BatchCompareEvidence? _batchCompareEvidence;

    private void InitializeBatchExportTab()
    {
        BatchExportList.ItemsSource = _batchExportReceipts;
        BatchMarkComparedButton.IsEnabled = false;
    }

    private void BatchSetDefaultOutput()
    {
        if (_batchRoot is null) return;
        _batchOutputRoot = BatchExportPlanner.DefaultOutputRoot(_batchRoot);
        BatchOutputFolderText.Text = _batchOutputRoot;
        ReloadBatchExportReceipts();
    }

    private void BatchChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _batchComparing || _batchRoot is null)
        {
            BatchStatus("先に解析フォルダを選択してください。");
            return;
        }
        var dlg = new OpenFolderDialog { Title = "一括出力先を選択（対象フォルダの外を推奨）" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            BatchExportPlanner.ValidateOutputRoot(_batchRoot, dlg.FolderName);
            _batchOutputRoot = Path.GetFullPath(dlg.FolderName);
            BatchOutputFolderText.Text = _batchOutputRoot;
            ReloadBatchExportReceipts();
            BatchStatus("出力先を変更しました。元ファイルは上書きしません。");
        }
        catch (Exception ex) { BatchStatus("出力先は利用できません: " + ex.Message); }
    }

    private void ReloadBatchExportReceipts()
    {
        if (_batchRoot is null || _batchOutputRoot is null) return;
        BatchCompareClear("出力済みファイルを選択して比較してください。");
        var old = BatchExportStore.Load(_batchRoot, _batchOutputRoot)
            .GroupBy(r => r.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _batchExportReceipts.Clear();
        foreach (var job in _batchJobs)
        {
            string destination = BatchExportPlanner.DestinationFor(job, _batchRoot, _batchOutputRoot);
            if (old.TryGetValue(job.SourcePath, out var previous) &&
                previous.SourceSize == job.SizeBytes &&
                previous.SourceModifiedTicks == job.ModifiedUtcTicks &&
                previous.Kind == job.Kind)
                _batchExportReceipts.Add(previous);
            else
                _batchExportReceipts.Add(new BatchExportReceipt(job.SourcePath, job.Kind,
                    job.SizeBytes, job.ModifiedUtcTicks, "", "", destination, BatchExportStatus.Pending));
        }
        BatchExportRefreshSummary();
    }

    private void BatchSaveExportReceipts()
    {
        if (_batchRoot is not null && _batchOutputRoot is not null)
            BatchExportStore.Save(_batchRoot, _batchOutputRoot, _batchExportReceipts.ToList());
    }

    private void BatchExportRefreshSummary()
    {
        BatchExportSummaryText.Text =
            $"出力済 {_batchExportReceipts.Count(x => x.Status == BatchExportStatus.Completed)} / " +
            $"要確認 {_batchExportReceipts.Count(x => x.Status == BatchExportStatus.NeedsReview)} / " +
            $"失敗 {_batchExportReceipts.Count(x => x.Status == BatchExportStatus.Failed)} / " +
            $"待機 {_batchExportReceipts.Count(x => x.Status == BatchExportStatus.Pending)} / " +
            $"比較記録 {_batchExportReceipts.Count(x => x.PreviewCheckedAtUtc is not null)}";
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static string FreeDestination(string proposed)
    {
        if (!File.Exists(proposed) && !Directory.Exists(proposed)) return proposed;
        string dir = Path.GetDirectoryName(proposed)!;
        string name = Path.GetFileNameWithoutExtension(proposed);
        string ext = Path.GetExtension(proposed);
        for (int i = 2; i <= 9999; i++)
        {
            string candidate = Path.Combine(dir, $"{name}__{i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("使用可能な出力名が見つかりません。");
    }

    private async void BatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _isDetecting || _videoExporting || _videoOpening || _videoScanning ||
            _videoTracking || _videoIndexing || _isLoading || _batchComparing) return;
        if (_batchRoot is null || _batchOutputRoot is null || _batchJobs.Count == 0)
        { BatchStatus("先にフォルダのスキャンと出力先設定を行ってください。"); return; }
        try { BatchExportPlanner.ValidateOutputRoot(_batchRoot, _batchOutputRoot); }
        catch (Exception ex) { BatchStatus(ex.Message); return; }
        if (MessageBox.Show(this,
            "確認済みの候補だけを書き出します。未確認・保留が残るファイルは出力しません。" +
            "動画はキーフレーム補間による試作の黒塗りです。検出漏れの可能性があるため、" +
            "出力後は画像・動画を必ず目視確認してください。続行しますか？",
            "R4.1 一括書き出し", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _batchRunning = true;
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;
        BatchRunButton.IsEnabled = false;
        BatchScanButton.IsEnabled = false;
        BatchExportButton.IsEnabled = false;
        ImageTab.IsEnabled = false;
        VideoTab.IsEnabled = false;
        try
        {
            // Reload a receipt list for exactly the current selected inventory and output location.
            ReloadBatchExportReceipts();
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                BatchJob job = _batchJobs[i];
                var previous = _batchExportReceipts[i];
                string baseTarget = BatchExportPlanner.DestinationFor(job, _batchRoot, _batchOutputRoot);
                var receipt = previous with { Status = BatchExportStatus.Running, Error = null };
                _batchExportReceipts[i] = receipt;
                BatchSaveExportReceipts();
                BatchStatus($"書き出し {i + 1}/{_batchJobs.Count}: {Path.GetFileName(job.SourcePath)}");
                try
                {
                    // Explicitly reviewed manual regions are exportable even without an AI model.
                    // The review gate below, not an AI batch status flag, is authoritative.
                    if (!BatchQueuePlanner.SignatureMatches(job))
                        throw new ReviewRequiredException("元ファイルが変更されました。再スキャンしてください。");

                    IReadOnlyList<DetectionRecord>? imageReviews = null;
                    IReadOnlyList<VideoMaskCandidate>? videoReviews = null;
                    string sourceHash = await HashAsync(job.SourcePath, ct);
                    ct.ThrowIfCancellationRequested();
                    if (!BatchQueuePlanner.SignatureMatches(job))
                        throw new ReviewRequiredException("ファイル内容の検査中に元ファイルが変わりました。");
                    string reviewSignature;
                    if (job.Kind == BatchMediaKind.Image)
                    {
                        Guid? sourceId = _history.FindSource(job.SourcePath, sourceHash);
                        if (sourceId is null)
                            throw new ReviewRequiredException("現在の画像に対応する確認履歴がありません。");
                        imageReviews = _history.GetDetections(sourceId.Value);
                        ValidateReviewed(imageReviews.Select(x => x.Decision));
                        reviewSignature = BatchExportPlanner.Fingerprint(JsonSerializer.Serialize(new
                        {
                            imageReviews,
                            Style = CurrentMaskStyle(),
                            Strength = (int)StrengthSlider.Value,
                            _settings.CropMargin
                        }));
                    }
                    else
                    {
                        videoReviews = VideoReviewStore.Load(job.SourcePath, sourceHash);
                        ValidateReviewed(videoReviews.Select(x => x.Decision));
                        reviewSignature = BatchExportPlanner.Fingerprint(JsonSerializer.Serialize(videoReviews));
                    }

                    // Reuse requires both the reviewed version and the exact previously written output.
                    if (previous.Status == BatchExportStatus.Completed &&
                        File.Exists(previous.OutputPath) &&
                        BatchQueuePlanner.IsUnder(previous.OutputPath, _batchOutputRoot) &&
                        BatchExportPlanner.Reusable(previous, job, sourceHash, reviewSignature,
                            previous.OutputPath, new FileInfo(previous.OutputPath).Length,
                            await HashAsync(previous.OutputPath, ct)))
                    {
                        _batchExportReceipts[i] = previous;
                        BatchStatus($"再利用: {Path.GetFileName(job.SourcePath)}（出力・確認内容とも未変更）");
                        continue;
                    }

                    string destination = FreeDestination(baseTarget);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    // Mark the intended output first; an interrupted export cannot be mistaken for completed.
                    _batchExportReceipts[i] = receipt with
                    {
                        SourceSha256 = sourceHash, ReviewSha256 = reviewSignature,
                        OutputPath = destination
                    };
                    BatchSaveExportReceipts();
                    // Encode to a private staging name first. Publish the final file only after
                    // confirming source + review have not changed; never expose partial output.
                    string staging = Path.Combine(Path.GetDirectoryName(destination)!, "." +
                        Path.GetFileNameWithoutExtension(destination) + "." +
                        Guid.NewGuid().ToString("N") + ".staged" + Path.GetExtension(destination));
                    try
                    {
                        if (job.Kind == BatchMediaKind.Image)
                        {
                            var pixels = await ImageCodec.ReadAsync(job.SourcePath, ct);
                            var accepted = imageReviews!.Where(x => x.Decision == ReviewDecision.Approved).ToArray();
                            var style = CurrentMaskStyle();
                            int strength = (int)StrengthSlider.Value;
                            double padding = _settings.CropMargin;
                            await Task.Run(() => ImageCodec.WritePng(staging,
                                MaskProcessor.Apply(pixels, accepted, style, strength, padding),
                                overwrite: false, cancellationToken: ct), ct);
                        }
                        else
                        {
                            var metadata = await _videoService.ProbeAsync(job.SourcePath, ct);
                            await _videoService.ExportBlackMasksAsync(job.SourcePath, staging,
                                metadata, videoReviews!, ct, overwrite: false);
                        }
                        // No success receipt if source or review changed while exporting.
                        if (!BatchQueuePlanner.SignatureMatches(job) ||
                            !string.Equals(sourceHash, await HashAsync(job.SourcePath, ct),
                                StringComparison.OrdinalIgnoreCase))
                            throw new IOException("出力中に元ファイルが変更されました。出力は確認が必要です。");
                        string currentReviewSignature;
                        if (job.Kind == BatchMediaKind.Image)
                        {
                            Guid? reviewedSource = _history.FindSource(job.SourcePath, sourceHash);
                            var current = reviewedSource is { } sid ? _history.GetDetections(sid) : new List<DetectionRecord>();
                            currentReviewSignature = BatchExportPlanner.Fingerprint(JsonSerializer.Serialize(new
                            {
                                imageReviews = current,
                                Style = CurrentMaskStyle(),
                                Strength = (int)StrengthSlider.Value,
                                _settings.CropMargin
                            }));
                        }
                        else
                            currentReviewSignature = BatchExportPlanner.Fingerprint(
                                JsonSerializer.Serialize(VideoReviewStore.Load(job.SourcePath, sourceHash)));
                        if (!string.Equals(currentReviewSignature, reviewSignature, StringComparison.Ordinal))
                            throw new IOException("書き出し中に判定が変更されました。結果の再確認が必要です。");
                        var outputInfo = new FileInfo(staging);
                        if (!outputInfo.Exists || outputInfo.Length == 0)
                            throw new IOException("出力ファイルが作成されていません。");
                        string outputHash = await HashAsync(staging, ct);
                        ct.ThrowIfCancellationRequested();
                        // Atomic, no-overwrite publication after both content and review are stable.
                        File.Move(staging, destination, overwrite: false);
                        _batchExportReceipts[i] = _batchExportReceipts[i] with
                        {
                            Status = BatchExportStatus.Completed,
                            OutputSize = outputInfo.Length, OutputSha256 = outputHash
                        };
                    }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                }
                catch (ReviewRequiredException ex)
                {
                    _batchExportReceipts[i] = _batchExportReceipts[i] with
                    { Status = BatchExportStatus.NeedsReview, Error = ex.Message };
                }
                catch (OperationCanceledException)
                {
                    _batchExportReceipts[i] = _batchExportReceipts[i] with
                    { Status = BatchExportStatus.Failed, Error = "中断されました。再度実行できます。" };
                    BatchSaveExportReceipts();
                    throw;
                }
                catch (Exception ex)
                {
                    _batchExportReceipts[i] = _batchExportReceipts[i] with
                    { Status = BatchExportStatus.Failed, Error = ex.Message.Length > 150 ? ex.Message[..150] : ex.Message };
                }
                finally
                {
                    BatchSaveExportReceipts();
                    BatchExportRefreshSummary();
                    await Task.Yield();
                }
            }
            BatchStatus("一括書き出しが終了しました。成功ファイルも安全検査済みではありません。必ず出力を再確認してください。");
        }
        catch (OperationCanceledException) { BatchStatus("一括書き出しを中止しました。成功済み出力は保持されます。"); }
        catch (Exception ex) { BatchStatus("一括書き出しエラー: " + ex.Message); }
        finally
        {
            _batchRunning = false;
            BatchRunButton.IsEnabled = true;
            BatchScanButton.IsEnabled = true;
            BatchExportButton.IsEnabled = true;
            ImageTab.IsEnabled = true;
            VideoTab.IsEnabled = true;
            _batchCts?.Dispose();
            _batchCts = null;
            BatchExportRefreshSummary();
        }
    }

    // Compare UI keeps decoded image pixels exclusively in memory. No crop/preview files are saved.
    private void BatchCompareClear(string message)
    {
        _batchCompareEvidence = null;
        if (BatchCompareBeforeImage is not null) BatchCompareBeforeImage.Source = null;
        if (BatchCompareAfterImage is not null) BatchCompareAfterImage.Source = null;
        if (BatchMarkComparedButton is not null) BatchMarkComparedButton.IsEnabled = false;
        if (BatchCompareStatusText is not null) BatchCompareStatusText.Text = message;
    }

    private void BatchShowCompare_Click(object sender, RoutedEventArgs e)
    {
        if (BatchExportList.SelectedItem is not BatchExportReceipt receipt ||
            receipt.Status != BatchExportStatus.Completed)
        {
            BatchStatus("比較するには「出力済み」の行を選んでください。");
            return;
        }
        MainTabs.SelectedItem = BatchCompareTab;
    }

    private void BatchExportList_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) =>
        BatchCompareClear("比較する出力を選び、「選択した出力を比較」を押してください。");

    private string CurrentReviewHashFor(BatchJob job, string sourceHash)
    {
        if (job.Kind == BatchMediaKind.Image)
        {
            Guid? sourceId = _history.FindSource(job.SourcePath, sourceHash);
            if (sourceId is null)
                throw new ReviewRequiredException("現在の画像に対応する確認履歴がありません。");
            var reviews = _history.GetDetections(sourceId.Value);
            ValidateReviewed(reviews.Select(x => x.Decision));
            return BatchExportPlanner.Fingerprint(JsonSerializer.Serialize(new
            {
                imageReviews = reviews,
                Style = CurrentMaskStyle(),
                Strength = (int)StrengthSlider.Value,
                _settings.CropMargin
            }));
        }
        var videoReviews = VideoReviewStore.Load(job.SourcePath, sourceHash);
        ValidateReviewed(videoReviews.Select(x => x.Decision));
        return BatchExportPlanner.Fingerprint(JsonSerializer.Serialize(videoReviews));
    }

    private async Task<(string sourceHash, string reviewHash, string outputHash)>
        VerifyBatchComparisonAsync(BatchJob job, BatchExportReceipt receipt, CancellationToken ct)
    {
        if (_batchOutputRoot is null || receipt.Status != BatchExportStatus.Completed ||
            !BatchQueuePlanner.IsUnder(receipt.OutputPath, _batchOutputRoot) ||
            !File.Exists(receipt.OutputPath) || !BatchQueuePlanner.SignatureMatches(job))
            throw new IOException("出力済み記録または元ファイルが無効です。再書き出ししてください。");
        string sourceHash = await HashAsync(job.SourcePath, ct);
        ct.ThrowIfCancellationRequested();
        string reviewHash = CurrentReviewHashFor(job, sourceHash);
        long outputSize = new FileInfo(receipt.OutputPath).Length;
        string outputHash = await HashAsync(receipt.OutputPath, ct);
        if (!BatchQueuePlanner.SignatureMatches(job) ||
            !string.Equals(sourceHash, await HashAsync(job.SourcePath, ct), StringComparison.OrdinalIgnoreCase) ||
            !BatchExportPlanner.Reusable(receipt, job, sourceHash, reviewHash,
                receipt.OutputPath, outputSize, outputHash))
            throw new IOException("元データ・確認記録・加工結果のいずれかが変更されました。再書き出ししてください。");
        return (sourceHash, reviewHash, outputHash);
    }

    private async void BatchCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning || _batchComparing || _videoExporting || _isLoading) return;
        if (BatchExportList.SelectedItem is not BatchExportReceipt receipt ||
            receipt.Status != BatchExportStatus.Completed)
        {
            BatchCompareClear("比較できるのは「出力済み」のファイルだけです。");
            return;
        }
        var job = _batchJobs.FirstOrDefault(x =>
            string.Equals(x.SourcePath, receipt.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (job is null) { BatchCompareClear("対象ファイルが一覧にありません。再スキャンしてください。"); return; }
        if (!double.TryParse(BatchCompareSecondsText.Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double second) ||
            !double.IsFinite(second) || second < 0 || second > 7 * 24 * 3600)
        {
            BatchCompareClear("動画の時刻を0以上の秒数で入力してください（例：12.5）。");
            return;
        }
        BatchCompareClear("内容の整合性を確認し、プレビューを作成中…");
        _batchComparing = true;
        BatchCompareButton.IsEnabled = false;
        BatchExportButton.IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var ct = timeout.Token;
            var (sourceHash, reviewHash, outputHash) =
                await VerifyBatchComparisonAsync(job, receipt, ct);
            if (!ReferenceEquals(BatchExportList.SelectedItem, receipt)) return;
            long timestampUs = 0;
            if (job.Kind == BatchMediaKind.Image)
            {
                var before = await ImageCodec.ReadAsync(job.SourcePath, ct);
                var after = await ImageCodec.ReadAsync(receipt.OutputPath, ct);
                if (before.Width != after.Width || before.Height != after.Height)
                    throw new InvalidDataException("出力画像の寸法が元データと一致していません。");
                BatchCompareBeforeImage.Source = ImageCodec.ToBitmap(
                    PreviewProcessor.Downsample(before, 720, 360));
                BatchCompareAfterImage.Source = ImageCodec.ToBitmap(
                    PreviewProcessor.Downsample(after, 720, 360));
            }
            else
            {
                var beforeMeta = await _videoService.ProbeAsync(job.SourcePath, ct);
                var afterMeta = await _videoService.ProbeAsync(receipt.OutputPath, ct);
                // FFmpeg exporter pads odd source dimensions to even for H.264/MP4.
                if (afterMeta.Width != (beforeMeta.Width + 1) / 2 * 2 ||
                    afterMeta.Height != (beforeMeta.Height + 1) / 2 * 2)
                    throw new InvalidDataException("出力動画の解像度が想定と一致していません。");
                long minDurationUs = Math.Min(beforeMeta.DurationUs, afterMeta.DurationUs);
                timestampUs = Math.Min((long)(second * 1_000_000), Math.Max(0, minDurationUs - 20_000));
                var beforePng = await _videoService.ExtractPngAsync(job.SourcePath,
                    timestampUs, beforeMeta.DurationUs, ct);
                var afterPng = await _videoService.ExtractPngAsync(receipt.OutputPath,
                    timestampUs, afterMeta.DurationUs, ct);
                BatchCompareBeforeImage.Source = ImageCodec.ToBitmap(
                    PreviewProcessor.Downsample(ImageCodec.ReadEncodedBytes(beforePng), 720, 360));
                BatchCompareAfterImage.Source = ImageCodec.ToBitmap(
                    PreviewProcessor.Downsample(ImageCodec.ReadEncodedBytes(afterPng), 720, 360));
            }
            if (!ReferenceEquals(BatchExportList.SelectedItem, receipt))
            {
                BatchCompareClear("選択が変わりました。もう一度比較してください。");
                return;
            }
            _batchCompareEvidence = new BatchCompareEvidence(job.SourcePath, sourceHash,
                reviewHash, outputHash, timestampUs);
            BatchMarkComparedButton.IsEnabled = true;
            BatchCompareStatusText.Text = job.Kind == BatchMediaKind.Image
                ? "画像の元・加工済みを比較中。目視したら「表示サンプルの確認を記録」を押してください。"
                : $"動画 {timestampUs / 1_000_000d:0.000} 秒の静止画比較。動画全編・全露出の安全判定ではありません。";
        }
        catch (Exception ex) { BatchCompareClear("比較できません: " + ex.Message); }
        finally
        {
            _batchComparing = false;
            BatchCompareButton.IsEnabled = true;
            BatchExportButton.IsEnabled = true;
        }
    }

    private async void BatchMarkCompared_Click(object sender, RoutedEventArgs e)
    {
        if (_batchComparing || _batchRunning || _batchCompareEvidence is not { } evidence ||
            BatchExportList.SelectedItem is not BatchExportReceipt receipt ||
            !string.Equals(receipt.SourcePath, evidence.SourcePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.SourceSha256, evidence.SourceHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.ReviewSha256, evidence.ReviewHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.OutputSha256, evidence.OutputHash, StringComparison.OrdinalIgnoreCase)) return;
        var job = _batchJobs.FirstOrDefault(x =>
            string.Equals(x.SourcePath, evidence.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (job is null) return;
        _batchComparing = true;
        BatchMarkComparedButton.IsEnabled = false;
        BatchCompareButton.IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var (sourceHash, reviewHash, outputHash) =
                await VerifyBatchComparisonAsync(job, receipt, timeout.Token);
            if (sourceHash != evidence.SourceHash || reviewHash != evidence.ReviewHash ||
                outputHash != evidence.OutputHash) throw new IOException("前回表示から内容が変更されました。");
            int index = _batchExportReceipts.IndexOf(receipt);
            if (index < 0) throw new InvalidOperationException("出力記録が更新されています。");
            var updated = receipt with
            {
                PreviewCheckedAtUtc = DateTimeOffset.UtcNow,
                PreviewCheckedAtUs = evidence.TimestampUs
            };
            _batchExportReceipts[index] = updated;
            BatchExportList.SelectedItem = updated;
            BatchSaveExportReceipts();
            BatchExportRefreshSummary();
            BatchCompareStatusText.Text = "表示サンプルの目視確認を記録しました。" +
                " 動画全体の確認や露出ゼロの証明ではありません。";
            _batchCompareEvidence = null;
        }
        catch (Exception ex) { BatchCompareClear("確認記録を保存できません: " + ex.Message); }
        finally { _batchComparing = false; BatchCompareButton.IsEnabled = true; }
    }

    private static void ValidateReviewed(IEnumerable<ReviewDecision> decisions)
    {
        var all = decisions.ToArray();
        if (all.Length == 0) throw new ReviewRequiredException("確認済みの検出候補がありません。");
        if (all.Any(x => x is ReviewDecision.Pending or ReviewDecision.Deferred))
            throw new ReviewRequiredException("未確認または保留の候補が残っています。");
        if (!all.Any(x => x == ReviewDecision.Approved))
            throw new ReviewRequiredException("隠す承認済み候補がありません。");
    }

    private sealed class ReviewRequiredException(string message) : Exception(message) { }
}
