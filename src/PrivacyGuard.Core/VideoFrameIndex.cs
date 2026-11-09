namespace PrivacyGuard.Core;

/// <summary>
/// Presentation-time index from ffprobe. No assumption of constant frame rate.
/// Values are elapsed microseconds relative to the first video frame.
/// </summary>
public sealed class VideoFrameIndex
{
    private readonly long[] _timestampsUs;
    public int Count => _timestampsUs.Length;
    public long FirstUs => _timestampsUs[0];
    public long LastUs => _timestampsUs[^1];

    public VideoFrameIndex(IEnumerable<long> frameTimestampsUs)
    {
        ArgumentNullException.ThrowIfNull(frameTimestampsUs);
        _timestampsUs = frameTimestampsUs.Distinct().Order().ToArray();
        if (_timestampsUs.Length == 0 || _timestampsUs[0] < 0)
            throw new ArgumentException("Frame index must contain nonnegative timestamps.", nameof(frameTimestampsUs));
    }

    public long Closest(long requestedUs)
    {
        int pos = Array.BinarySearch(_timestampsUs, requestedUs);
        if (pos >= 0) return _timestampsUs[pos];
        pos = ~pos;
        if (pos == 0) return FirstUs;
        if (pos == Count) return LastUs;
        return requestedUs - _timestampsUs[pos - 1] <= _timestampsUs[pos] - requestedUs
            ? _timestampsUs[pos - 1] : _timestampsUs[pos];
    }

    /// <summary>Advance from nearest presentation time, then return adjacent distinct frame time.</summary>
    public long Adjacent(long currentUs, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        long closest = Closest(currentUs);
        int index = Array.BinarySearch(_timestampsUs, closest);
        return _timestampsUs[Math.Clamp(index + direction, 0, Count - 1)];
    }
}
