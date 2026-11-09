namespace PrivacyGuard.Core;

public enum CaptureRecoveryPhase
{
    Connecting,
    Processing,
    Disconnected,
    RetryWait,
    Stopped,
    ModelFault
}

public sealed record CaptureRecoveryEvent(CaptureRecoveryPhase Phase, int Attempt, string Detail);

/// <summary>
/// Retries capture acquisition after disconnects. Every reconnect uses a fresh
/// input enumerator but one monotonic gate sequence, and immediately blacks
/// the downstream watchdog during errors/backoff. Does not retry a faulted AI.
/// </summary>
public sealed class LiveCaptureRecovery
{
    private readonly Func<CancellationToken, IAsyncEnumerable<LiveInputFrame>> _acquire;
    private readonly LiveSafetyGate _gate;
    private readonly ILiveFrameSink _sink;
    private readonly Action _blackout;
    private readonly Action<CaptureRecoveryEvent>? _status;
    private readonly TimeSpan _firstDelay;
    private readonly TimeSpan _maxDelay;

    public LiveCaptureRecovery(
        Func<CancellationToken, IAsyncEnumerable<LiveInputFrame>> acquire,
        LiveSafetyGate gate,
        ILiveFrameSink sink,
        Action blackout,
        Action<CaptureRecoveryEvent>? status = null,
        TimeSpan? firstDelay = null, TimeSpan? maxDelay = null)
    {
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _blackout = blackout ?? throw new ArgumentNullException(nameof(blackout));
        _status = status;
        _firstDelay = firstDelay ?? TimeSpan.FromMilliseconds(300);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(5);
        if (_firstDelay < TimeSpan.FromMilliseconds(10) ||
            _maxDelay < _firstDelay || _maxDelay > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(firstDelay));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        long sequence = 0;
        int failures = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_gate.IsFaulted)
                {
                    _blackout();
                    _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.ModelFault, failures,
                        "Detector fault requires a new gate; capture not resumed."));
                    break;
                }
                _blackout();
                _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.Connecting, failures + 1,
                    "Connecting to video capture."));
                try
                {
                    await foreach (var raw in _acquire(ct).WithCancellation(ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        // A device reconnect may reset its own numbering to zero.
                        // Never pass those reset values into the same inference gate.
                        var result = await _gate.ProcessAsync(sequence++, raw.Pixels, ct);
                        if (result.Disposition is LiveFrameDisposition.DetectorFault
                            or LiveFrameDisposition.InvalidDetection
                            or LiveFrameDisposition.TimedOut)
                        {
                            _blackout();
                            throw new LiveDetectorFaultException(result.Reason);
                        }
                        await _sink.WriteAsync(result, ct);
                        if (failures != 0)
                        {
                            failures = 0;
                            _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.Processing, 0,
                                "Capture reconnected; processing resumed."));
                        }
                    }
                    throw new IOException("Capture reached end of stream.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _blackout();
                    if (_gate.IsFaulted || ex is LiveDetectorFaultException)
                    {
                        _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.ModelFault, failures,
                            "AI processing failed and was disabled: " + ex.GetType().Name));
                        break;
                    }
                    failures = Math.Min(1000, failures + 1);
                    _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.Disconnected, failures,
                        "Capture disconnected: " + ex.GetType().Name));
                    double multiplier = Math.Pow(2, Math.Min(failures - 1, 8));
                    var delay = TimeSpan.FromMilliseconds(
                        Math.Min(_maxDelay.TotalMilliseconds, _firstDelay.TotalMilliseconds * multiplier));
                    _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.RetryWait, failures,
                        "Retry capture after blackout."));
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                }
            }
        }
        finally
        {
            _blackout();
            _status?.Invoke(new CaptureRecoveryEvent(CaptureRecoveryPhase.Stopped, failures,
                "Video output blacked out."));
        }
    }

    private sealed class LiveDetectorFaultException(string message) : Exception(message);
}
