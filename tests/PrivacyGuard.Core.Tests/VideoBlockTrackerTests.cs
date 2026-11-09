using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class VideoBlockTrackerTests
{
    private const int W = 160, H = 112;
    private static NormalizedRect Region(int x, int y, int w = 28, int h = 28) =>
        new(x / (double)W, y / (double)H, w / (double)W, h / (double)H);

    private static PixelBuffer Scene(int x, int y, bool textured = true)
    {
        byte[] data = new byte[W * H * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = 20; data[i+1] = 20; data[i+2] = 20; data[i+3] = 255;
        }
        for (int yy = 0; yy < 28; yy++)
        for (int xx = 0; xx < 28; xx++)
        {
            int p = ((y + yy) * W + x + xx) * 4;
            byte v = textured ? (byte)(60 + (xx * 23 + yy * 17 + xx * yy * 3) % 195) : (byte)20;
            data[p] = data[p+1] = data[p+2] = v;
        }
        return new PixelBuffer(W, H, data, copy: false);
    }

    [Fact]
    public void TextureTranslation_IsProposedWithoutChangingSize()
    {
        var start = Region(45, 37);
        var step = VideoBlockTracker.Estimate(Scene(45,37), Scene(51,41), start);
        Assert.True(step.Accepted, step.Reason);
        Assert.Equal(51/(double)W, step.Rect.X, 2);
        Assert.Equal(41/(double)H, step.Rect.Y, 2);
        Assert.Equal(start.Width, step.Rect.Width);
    }

    [Fact]
    public void UniformPatch_StopsWithLowTexture()
    {
        var step = VideoBlockTracker.Estimate(Scene(45,37,false), Scene(51,41,false), Region(45,37));
        Assert.False(step.Accepted);
        Assert.Equal("low_texture", step.Reason);
    }

    [Fact]
    public void LargeJump_DoesNotFabricateContinuousTrack()
    {
        var step = VideoBlockTracker.Estimate(Scene(35,30), Scene(105,63), Region(35,30));
        Assert.False(step.Accepted);
    }

    private static async IAsyncEnumerable<(long TimestampUs, PixelBuffer Pixels)> Frames(bool jump)
    {
        for (int i=0; i<8; i++)
        {
            int x = jump && i >= 4 ? 110 : 40 + i * 3;
            yield return (i * 125_000L, Scene(x, 35));
            await Task.Yield();
        }
    }

    [Fact]
    public async Task SuccessfulProposal_ContainsPendingKeyframes()
    {
        var proposal = await VideoBlockTracker.ProposeAsync(Frames(false),
            Region(40,35), 0, 1_000_000, 8, CancellationToken.None);
        Assert.True(proposal.Completed, proposal.Reason);
        Assert.Equal(8, proposal.Keys.Count);
        Assert.Null(proposal.BreakUs);
        Assert.True(proposal.AverageQuality > .5);
    }

    [Fact]
    public async Task BrokenSequence_FailsClosed_NoInterpolationBeyondBreak()
    {
        var proposal = await VideoBlockTracker.ProposeAsync(Frames(true),
            Region(40,35), 0, 1_000_000, 8, CancellationToken.None);
        Assert.False(proposal.Completed);
        Assert.True(proposal.Keys.Count < 8);
        Assert.NotNull(proposal.BreakUs);
    }

    [Fact]
    public async Task OverlongInterval_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            VideoBlockTracker.ProposeAsync(Frames(false), Region(40,35),
                0, 16_000_000, 8, CancellationToken.None));
    }
}
