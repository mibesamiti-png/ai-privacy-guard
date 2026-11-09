using System.Buffers.Binary;
using System.Diagnostics;

namespace PrivacyGuard.Core;

/// <summary>
/// The OBS output side independently checks whether a gated frame is fresh.
/// A stalled camera or inference loop must not leave the last image visible forever.
/// This only protects a *running* bridge process; OBS renderer crash/scene routing
/// and undetected content remain separate risks.
/// </summary>
public sealed class LiveOutputWatchdog : ILiveFrameSink
{
    private readonly object _sync = new();
    private readonly int _width;
    private readonly int _height;
    private readonly TimeSpan _maxFrameAge;
    private readonly Func<long> _ticks;
    private readonly long _frequency;
    private PixelBuffer? _latest;
    private long _latestTick;
    private long _lastSequence = -1;

    public LiveOutputWatchdog(int width, int height, TimeSpan maxFrameAge,
        Func<long>? monotonicTimestamp = null, long timestampFrequency = 0)
    {
        if (width < 1 || height < 1 || (long)width * height > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (maxFrameAge < TimeSpan.FromMilliseconds(50) ||
            maxFrameAge > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maxFrameAge));
        _width = width;
        _height = height;
        _maxFrameAge = maxFrameAge;
        _ticks = monotonicTimestamp ?? Stopwatch.GetTimestamp;
        _frequency = timestampFrequency == 0 ? Stopwatch.Frequency : timestampFrequency;
        if (_frequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
    }

    public int Width => _width;
    public int Height => _height;
    public TimeSpan MaxFrameAge => _maxFrameAge;

    /// <summary>Only results that passed through the gate may enter this sink.</summary>
    public ValueTask WriteAsync(LiveFrameResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        ct.ThrowIfCancellationRequested();
        if (result.Pixels.Width != _width || result.Pixels.Height != _height)
            throw new InvalidDataException("Wrong frame dimensions for OBS output.");

        lock (_sync)
        {
            if (result.Sequence <= _lastSequence)
            {
                _latest = null;
                return ValueTask.CompletedTask;
            }
            _lastSequence = result.Sequence;
            // Even an incorrectly populated blocked result cannot leak pixels.
            _latest = result.Disposition is LiveFrameDisposition.Screened or LiveFrameDisposition.Masked
                ? new PixelBuffer(_width, _height, result.Pixels.Bgra)
                : null;
            _latestTick = _ticks();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Force blackout immediately, e.g. on capture disconnect/shutdown.</summary>
    public void ForceBlackout()
    {
        lock (_sync) _latest = null;
    }

    /// <summary>
    /// Returns a detached pixel buffer for the output. No capture activity,
    /// late frame, or detector error means solid opaque black.
    /// </summary>
    public PixelBuffer Snapshot()
    {
        lock (_sync)
        {
            if (_latest is null) return Black();
            long elapsedTicks = _ticks() - _latestTick;
            if (elapsedTicks < 0 || elapsedTicks / (double)_frequency > _maxFrameAge.TotalSeconds)
            {
                _latest = null;
                return Black();
            }
            return new PixelBuffer(_width, _height, _latest.Bgra);
        }
    }

    private PixelBuffer Black()
    {
        var pixels = new byte[checked(_width * _height * 4)];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new PixelBuffer(_width, _height, pixels, copy: false);
    }
}

/// <summary>
/// Minimal 32-bit top-down Windows BMP representation of BGRA pixels. Browser
/// Source can display this with no JPEG library, at moderate demo resolutions.
/// </summary>
public static class BmpFrameEncoder
{
    public static byte[] Encode(PixelBuffer frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int pixelBytes = checked(frame.Width * frame.Height * 4);
        int total = checked(54 + pixelBytes);
        var bytes = new byte[total];
        bytes[0] = 0x42; bytes[1] = 0x4d; // 'BM'
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), total);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), -frame.Height); // top-down
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), pixelBytes);
        frame.Bgra.AsSpan().CopyTo(bytes.AsSpan(54));
        // BMP BI_RGB32 ignores high byte in most decoders. Force opaque for
        // compatibility even when the original contains transparent pixels.
        for (int i = 57; i < bytes.Length; i += 4) bytes[i] = 255;
        return bytes;
    }
}
