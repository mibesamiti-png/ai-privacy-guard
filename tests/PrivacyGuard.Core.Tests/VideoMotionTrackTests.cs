using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public class VideoMotionTrackTests
{
    private static VideoMaskCandidate Motion(params VideoMaskKeyframe[] points) =>
        new(Guid.NewGuid(), 0, 2_000_000, new NormalizedRect(0.1, 0.2, 0.2, 0.2),
            ReviewDecision.Approved, "MANUAL_REGION", "manual", Keyframes: points);

    [Fact]
    public void InterpolateAtMidpoint_ChangesPositionAndSize()
    {
        var candidate = Motion(
            new(0, new NormalizedRect(0.1, 0.2, 0.2, 0.2)),
            new(1_000_000, new NormalizedRect(0.5, 0.4, 0.4, 0.3)));
        var r = candidate.BoundsAt(500_000);
        Assert.Equal(0.3, r.X, 5);
        Assert.Equal(0.3, r.Y, 5);
        Assert.Equal(0.3, r.Width, 5);
        Assert.Equal(0.25, r.Height, 5);
    }

    [Fact]
    public void InterpolateClampsOutsideKeyframes()
    {
        var first = new NormalizedRect(0.1, 0.1, 0.2, 0.2);
        var last = new NormalizedRect(0.6, 0.6, 0.2, 0.2);
        var candidate = Motion(new(250_000, first), new(1_750_000, last));
        Assert.Equal(first, candidate.BoundsAt(0));
        Assert.Equal(last, candidate.BoundsAt(2_000_000));
    }

    [Fact]
    public void OneOrZeroKeyframe_StaysStatic()
    {
        var one = Motion(new(0, new NormalizedRect(0.8, 0.8, 0.1, 0.1)));
        Assert.Equal(one.Bounds, one.BoundsAt(500_000));
        Assert.False(one.IsMoving);
        Assert.Equal(one.Bounds, Motion().BoundsAt(1_000_000));
    }

    [Fact]
    public void UpsertReplacesSameTimestampAndSorts()
    {
        VideoMaskKeyframe[] old = [
            new(1_000_000, new NormalizedRect(0.1, 0.1, 0.1, 0.1)),
            new(500_000, new NormalizedRect(0.2, 0.2, 0.1, 0.1))
        ];
        var result = VideoMotionTrack.Upsert(old,
            new(500_000, new NormalizedRect(0.4, 0.4, 0.1, 0.1)));
        Assert.Equal(2, result.Count);
        Assert.Equal(500_000, result[0].TimestampUs);
        Assert.Equal(0.4, result[0].Bounds.X, 5);
    }

    [Fact]
    public void RejectInvalidKeyframeBoundsOrTimestamp()
    {
        var nonFinite = Motion(new(0, new NormalizedRect(double.NaN, 0.2, 0.2, 0.2)));
        Assert.Throws<ArgumentException>(() => VideoMaskPlan.Validate(nonFinite, 2_000_000));
        var duplicate = Motion(new(0, new NormalizedRect(0.1, 0.1, 0.2, 0.2)),
            new(0, new NormalizedRect(0.2, 0.2, 0.2, 0.2)));
        Assert.Throws<ArgumentException>(() => VideoMaskPlan.Validate(duplicate, 2_000_000));
        var outside = Motion(new(2_100_000, new NormalizedRect(0.2, 0.2, 0.2, 0.2)));
        Assert.Throws<ArgumentException>(() => VideoMaskPlan.Validate(outside, 2_000_000));
    }

    [Fact]
    public void ExportScriptIncludesMovingCommandsAndLeavesUnapprovedOut()
    {
        var motion = Motion(new(0, new NormalizedRect(0.1, 0.1, 0.1, 0.1)),
            new(1_000_000, new NormalizedRect(0.4, 0.1, 0.1, 0.1)));
        var hidden = Motion() with { Decision = ReviewDecision.Pending };
        var result = VideoMotionExportPlan.Build(new[] { motion, hidden },
            200, 100, 2_000_000, 10, "apg_cmd.txt");
        Assert.Contains("sendcmd=f=apg_cmd.txt", result.Filter);
        Assert.Contains("drawbox@guard0", result.Filter);
        Assert.DoesNotContain("guard1", result.Filter);
        Assert.Contains("drawbox@guard0 x", result.CommandText);
        Assert.True(result.CommandCount > 0);
    }

    [Fact]
    public void StaticExportScriptDoesNotCreateCommandFile()
    {
        var result = VideoMotionExportPlan.Build(new[] { Motion() },
            200, 100, 2_000_000, 30, "safe.txt");
        Assert.Equal(string.Empty, result.CommandText);
        Assert.DoesNotContain("sendcmd", result.Filter);
    }

    [Fact]
    public void ExportRejectsUnsafeFilenameAndUnapprovedOnly()
    {
        Assert.Throws<ArgumentException>(() => VideoMotionExportPlan.Build(new[] { Motion() },
            100, 100, 2_000_000, 30, "../path.txt"));
        Assert.Throws<InvalidOperationException>(() => VideoMotionExportPlan.Build(
            new[] { Motion() with { Decision = ReviewDecision.Pending } },
            100, 100, 2_000_000, 30, "safe.txt"));
    }
}
