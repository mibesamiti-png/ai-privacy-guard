using System.Buffers.Binary;
using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class LiveOutputWatchdogTests
{
    private static PixelBuffer Frame(byte value = 123)
    {
        byte[] data = new byte[4 * 3 * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = value;
            data[i + 1] = value;
            data[i + 2] = value;
            data[i + 3] = 255;
        }
        return new PixelBuffer(4, 3, data, copy: false);
    }

    private static LiveFrameResult Screened(long seq, byte value = 123) =>
        new(seq, Frame(value), LiveFrameDisposition.Screened, "synthetic");

    private static void IsBlack(PixelBuffer pixels)
    {
        for (int i = 0; i < pixels.Bgra.Length; i += 4)
        {
            Assert.Equal((byte)0, pixels.Bgra[i]);
            Assert.Equal((byte)0, pixels.Bgra[i + 1]);
            Assert.Equal((byte)0, pixels.Bgra[i + 2]);
            Assert.Equal((byte)255, pixels.Bgra[i + 3]);
        }
    }

    [Fact]
    public void DefaultOutputIsOpaqueBlack()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromMilliseconds(500));
        IsBlack(w.Snapshot());
    }

    [Fact]
    public async Task FreshSanitizedFrameVisibleThenStopsAfterTimeout()
    {
        long now = 5000;
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromMilliseconds(500),
            () => now, timestampFrequency: 1000);
        await w.WriteAsync(Screened(0), CancellationToken.None);
        Assert.Equal((byte)123, w.Snapshot().Bgra[0]);
        now += 501;
        IsBlack(w.Snapshot());
        now -= 500; // even rolling clock backwards cannot restore expired pixels
        IsBlack(w.Snapshot());
    }

    [Fact]
    public async Task BlockedResultIsNeverPassedThroughEvenIfPixelsAreColored()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromSeconds(1));
        var blocked = new LiveFrameResult(0, Frame(), LiveFrameDisposition.DetectorFault, "failure");
        await w.WriteAsync(blocked, CancellationToken.None);
        IsBlack(w.Snapshot());
    }

    [Fact]
    public async Task ForceBlackoutPreventsStaleFramesFromReappearing()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromSeconds(1));
        await w.WriteAsync(Screened(3), CancellationToken.None);
        w.ForceBlackout();
        IsBlack(w.Snapshot());
        await w.WriteAsync(Screened(2), CancellationToken.None); // out of order
        IsBlack(w.Snapshot());
    }

    [Fact]
    public async Task SnapshotIsAnIndependentCopy()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromSeconds(1));
        var frame = Screened(0);
        await w.WriteAsync(frame, CancellationToken.None);
        frame.Pixels.Bgra[0] = 0;
        var first = w.Snapshot();
        Assert.Equal((byte)123, first.Bgra[0]);
        first.Bgra[0] = 5;
        Assert.Equal((byte)123, w.Snapshot().Bgra[0]);
    }

    [Fact]
    public async Task OutOfOrderResultClearsPreviousFrame()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromSeconds(1));
        await w.WriteAsync(Screened(7), CancellationToken.None);
        await w.WriteAsync(Screened(7), CancellationToken.None);
        IsBlack(w.Snapshot());
    }

    [Fact]
    public async Task RejectsWrongDimensionsAndKeepsExistingFrame()
    {
        var w = new LiveOutputWatchdog(4, 3, TimeSpan.FromSeconds(1));
        await w.WriteAsync(Screened(0), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await w.WriteAsync(new LiveFrameResult(1, new PixelBuffer(1, 1, new byte[4]),
                LiveFrameDisposition.Masked, "invalid"), CancellationToken.None));
        Assert.Equal((byte)123, w.Snapshot().Bgra[0]);
    }

    [Fact]
    public void BmpEncoderOutputsTopDownOpaqueImage()
    {
        var frame = Frame();
        frame.Bgra[3] = 0;
        var bmp = BmpFrameEncoder.Encode(frame);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(54 + 4 * 3 * 4, bmp.Length);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18)));
        Assert.Equal(-3, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22)));
        Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28)));
        Assert.Equal((byte)123, bmp[54]);
        Assert.Equal((byte)255, bmp[57]);
        Assert.Equal((byte)0, frame.Bgra[3]); // original untouched
    }

    [Fact]
    public void InvalidGeometryAndTimeoutAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveOutputWatchdog(0, 3, TimeSpan.FromMilliseconds(500)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveOutputWatchdog(4, 3, TimeSpan.Zero));
    }
}
