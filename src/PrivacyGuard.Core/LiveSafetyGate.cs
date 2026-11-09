namespace PrivacyGuard.Core;

/// <summary>
/// An experimental per-frame processing gate. This is NOT evidence that a model
/// recognizes sensitive exposures and is not a certified live-broadcast safety system.
/// Every failure in the detection path produces a new opaque black frame.
/// </summary>
public enum LiveFrameDisposition
{
    Blocked,
    Screened,
    Masked,
    TimedOut,
    DetectorFault,
    InvalidDetection,
    Busy,
    OutOfOrder
}

public sealed record LiveFrameResult(
    long Sequence,
    PixelBuffer Pixels,
    LiveFrameDisposition Disposition,
    string Reason)
{
    // A "Screened" image may still contain missed exposures. The name reflects
    // whether the detector ran, not a guarantee about its classification.
    public bool DetectionCompleted =>
        Disposition is LiveFrameDisposition.Screened or LiveFrameDisposition.Masked;
}

public sealed record LiveGateSettings(TimeSpan DetectionTimeout, double MaskPadding = 0.25)
{
    public static LiveGateSettings Default => new(TimeSpan.FromMilliseconds(250), 0.25);

    public void Validate()
    {
        if (DetectionTimeout < TimeSpan.FromMilliseconds(10) ||
            DetectionTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(DetectionTimeout));
        if (!double.IsFinite(MaskPadding) || MaskPadding is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MaskPadding));
    }
}

public sealed record LiveInputFrame(long Sequence, PixelBuffer Pixels);

public interface ILiveFrameSink
{
    /// <summary>The sink accepts only results from the gate, never raw camera frames.</summary>
    ValueTask WriteAsync(LiveFrameResult result, CancellationToken ct);
}

/// <summary>
/// Serializes detector use, rejects stale or concurrent frames, and remains
/// disarmed after inference timeout/fault. The caller owns detector lifetime.
/// </summary>
public sealed class LiveSafetyGate
{
    private readonly IExposureDetector? _detector;
    private readonly LiveGateSettings _settings;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private volatile bool _experimentalEnabled;
    private volatile bool _faulted;
    private long _lastSequence = -1;

    public LiveSafetyGate(IExposureDetector? detector, LiveGateSettings? settings = null)
    {
        _detector = detector;
        _settings = settings ?? LiveGateSettings.Default;
        _settings.Validate();
    }

    public bool IsExperimentalProcessingEnabled => _experimentalEnabled && !_faulted && _detector is not null;
    public bool IsFaulted => _faulted;

    /// <summary>
    /// Must be explicitly called before any detector-screened frame is emitted.
    /// This merely enables experimental processing; it does not certify the model.
    /// </summary>
    public void EnableExperimentalProcessing()
    {
        if (_detector is null)
            throw new InvalidOperationException("A validated compatible model must be configured.");
        if (_faulted)
            throw new InvalidOperationException("The gate faulted; create a new gate after diagnosis.");
        _experimentalEnabled = true;
    }

    public void Disable() => _experimentalEnabled = false;

    public async Task<LiveFrameResult> ProcessAsync(
        long sequence, PixelBuffer input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (sequence < 0) return Black(sequence, input, LiveFrameDisposition.OutOfOrder, "Invalid frame sequence.");

        // Live workloads never queue stale frames behind a slow inference.
        if (!await _serial.WaitAsync(0))
            return Black(sequence, input, LiveFrameDisposition.Busy, "Inference is still busy.");

        try
        {
            if (sequence <= _lastSequence)
                return Black(sequence, input, LiveFrameDisposition.OutOfOrder, "Out-of-order frame.");
            _lastSequence = sequence;

            if (!IsExperimentalProcessingEnabled)
                return Black(sequence, input, LiveFrameDisposition.Blocked, "Processing disabled or model unavailable.");
            if (ct.IsCancellationRequested)
                return Black(sequence, input, LiveFrameDisposition.Blocked, "Cancelled.");

            // WaitAsync enforces a bound on output latency even if a detector
            // ignores cancellation. A timeout permanently disarms this gate;
            // the timed-out detector may still be running in the background.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_settings.DetectionTimeout);
            IReadOnlyList<DetectionCandidate> candidates;
            try
            {
                candidates = await _detector!.DetectAsync(input, linked.Token)
                    .WaitAsync(_settings.DetectionTimeout, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Disable();
                return Black(sequence, input, LiveFrameDisposition.Blocked, "Capture cancelled.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _faulted = true;
                Disable();
                return Black(sequence, input, LiveFrameDisposition.TimedOut, "Detector timed out.");
            }
            catch (TimeoutException)
            {
                _faulted = true;
                Disable();
                return Black(sequence, input, LiveFrameDisposition.TimedOut, "Detector timed out.");
            }
            catch (Exception)
            {
                _faulted = true;
                Disable();
                return Black(sequence, input, LiveFrameDisposition.DetectorFault, "Detector raised an exception.");
            }

            // A stop request racing with inference must never pass a raw frame.
            if (!IsExperimentalProcessingEnabled || ct.IsCancellationRequested)
                return Black(sequence, input, LiveFrameDisposition.Blocked, "Processing stopped.");

            if (candidates is null || candidates.Count > 1000 || candidates.Any(c =>
                string.IsNullOrWhiteSpace(c.Category) ||
                !double.IsFinite(c.Score) || c.Score < 0 || c.Score > 1 ||
                !double.IsFinite(c.Bounds.X) || !double.IsFinite(c.Bounds.Y) ||
                !double.IsFinite(c.Bounds.Width) || !double.IsFinite(c.Bounds.Height) ||
                c.Bounds.Width <= 0 || c.Bounds.Height <= 0 ||
                c.Bounds.X < 0 || c.Bounds.Y < 0 ||
                c.Bounds.X + c.Bounds.Width > 1.000001 ||
                c.Bounds.Y + c.Bounds.Height > 1.000001))
            {
                _faulted = true;
                Disable();
                return Black(sequence, input, LiveFrameDisposition.InvalidDetection, "Invalid model output.");
            }

            if (candidates.Count == 0)
                return new LiveFrameResult(sequence,
                    new PixelBuffer(input.Width, input.Height, input.Bgra),
                    LiveFrameDisposition.Screened,
                    "Detector returned no candidates; this is NOT a safety guarantee.");

            // Black regions are deliberately used for the experimental live
            // prototype. Blur or pixelation may reveal details at weak strength.
            var regions = candidates.Select(c =>
                DetectionRecord.Manual(Guid.Empty, c.Bounds) with
                { Decision = ReviewDecision.Approved }).ToArray();
            var result = MaskProcessor.Apply(input, regions, MaskStyle.Black,
                padding: _settings.MaskPadding);
            return new LiveFrameResult(sequence, result,
                LiveFrameDisposition.Masked, "Detector candidates covered.");
        }
        finally
        {
            _serial.Release();
        }
    }

    public static LiveFrameResult Black(
        long sequence, PixelBuffer template, LiveFrameDisposition disposition, string reason)
    {
        var pixels = new byte[template.Bgra.Length];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new LiveFrameResult(sequence,
            new PixelBuffer(template.Width, template.Height, pixels, copy: false),
            disposition, reason);
    }
}

/// <summary>
/// Forwards only gate results to the output. Capture/output failure handling
/// and a true watchdog-backed OBS output belong to R5.1+.
/// </summary>
public static class LiveFramePipeline
{
    public static async Task RunAsync(
        IAsyncEnumerable<LiveInputFrame> source,
        LiveSafetyGate gate,
        ILiveFrameSink sink,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(sink);

        await foreach (var frame in source.WithCancellation(ct))
        {
            var processed = await gate.ProcessAsync(frame.Sequence, frame.Pixels, ct);
            await sink.WriteAsync(processed, ct);
        }
    }
}
