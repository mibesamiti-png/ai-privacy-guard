using PrivacyGuard.Core;
using System.Text.Json;
using Xunit;

public sealed class ValidationLabProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apg-lab-tests-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(_root, "lab.json");

    public ValidationLabProjectTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeFile(string data = "synthetic sample")
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(path, data);
        return path;
    }

    [Fact]
    public void NewProject_StartsUnknownNotVerifiedNegative()
    {
        var lab = ValidationLabStore.Open(Project);
        Assert.Empty(lab.Sources);
        Assert.Empty(lab.Bookmarks);
        Assert.Empty(lab.Regions);
        Assert.False(lab.CanUndo);
    }

    [Fact]
    public async Task ExplicitRightsRequiredAndDuplicatesDeduped()
    {
        var lab = ValidationLabStore.Open(Project);
        string path = MakeFile();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lab.AddLocalSourceAsync(path, LabMediaKind.Image, LabVisualDomain.Illustration2D, false));
        var first = await lab.AddLocalSourceAsync(path, LabMediaKind.Image,
            LabVisualDomain.Illustration2D, true);
        var second = await lab.AddLocalSourceAsync(path, LabMediaKind.Image,
            LabVisualDomain.Illustration2D, true);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(LabTruthState.Unknown, first.Truth);
        Assert.Equal(LabReviewState.Unreviewed, first.ReviewState);
        Assert.Equal(64, first.Sha256.Length);
        Assert.True(await lab.IsSourceCurrentAsync(first.Id));
        Assert.Single(ValidationLabStore.Open(Project).Sources);
    }

    [Fact]
    public async Task ModifiedSourceCannotSilentlyReuseOldGroundTruth()
    {
        var lab = ValidationLabStore.Open(Project);
        string path = MakeFile();
        var original = await lab.AddLocalSourceAsync(path, LabMediaKind.Image,
            LabVisualDomain.Photograph, true);
        File.WriteAllText(path, "changed");
        Assert.False(await lab.IsSourceCurrentAsync(original.Id));
        var modified = await lab.AddLocalSourceAsync(path, LabMediaKind.Image,
            LabVisualDomain.Photograph, true);
        Assert.NotEqual(original.Id, modified.Id);
        Assert.Equal(LabTruthState.Unknown, modified.Truth);
        Assert.Equal(LabReviewState.Unreviewed, modified.ReviewState);
        Assert.Equal(2, lab.Sources.Count);
    }

    [Fact]
    public async Task ExplicitlyReviewedImageMayBeNegativeAndUndoneAcrossRestart()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Image,
            LabVisualDomain.Photograph, true);
        Assert.Throws<InvalidOperationException>(() => lab.MarkImageNegative(source.Id, false));
        lab.MarkImageNegative(source.Id, explicitlyInspected: true);
        Assert.Equal(LabTruthState.Negative, ValidationLabStore.Open(Project).Sources.Single().Truth);
        lab = ValidationLabStore.Open(Project);
        Assert.True(lab.CanUndo);
        Assert.True(lab.UndoLastSourceEdit());
        Assert.Equal(LabTruthState.Unknown, ValidationLabStore.Open(Project).Sources.Single().Truth);
        Assert.False(lab.CanUndo);
    }

    [Fact]
    public async Task VideoCannotBeMarkedEntirelyNegativeWithoutCoverage()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile("synthetic video"), LabMediaKind.Video,
            LabVisualDomain.Model3D, true);
        Assert.Throws<InvalidOperationException>(() => lab.MarkImageNegative(source.Id, true));
        Assert.Equal(LabReviewState.Unreviewed, lab.Sources.Single().ReviewState);
    }

    [Fact]
    public async Task LocalPositiveRegionNeedsValidBoundsAndTimeThenRevertsCorrectly()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Video,
            LabVisualDomain.Model3D, true);
        Assert.Throws<ArgumentException>(() => lab.AddPositiveRegion(source.Id,
            new NormalizedRect(.9, .3, .4, .4), "candidate", 1234, 1300));
        Assert.Throws<ArgumentException>(() => lab.AddPositiveRegion(source.Id,
            new NormalizedRect(.1, .2, .3, .4), "candidate"));
        Assert.Throws<ArgumentException>(() => lab.AddPositiveRegion(source.Id,
            new NormalizedRect(.1, .2, .3, .4), "candidate", 2000, 1000));
        var region = lab.AddPositiveRegion(source.Id, new NormalizedRect(.1, .2, .3, .4),
            "manually-reviewed", startUs: 200_000, endUs: 350_000);
        Assert.Equal(200_000, region.StartUs);
        Assert.Equal(LabTruthState.Positive, lab.Sources.Single().Truth);
        Assert.Equal(LabReviewState.PartiallyReviewed, lab.Sources.Single().ReviewState);
        Assert.Single(ValidationLabStore.Open(Project).Regions);
        lab = ValidationLabStore.Open(Project);
        Assert.True(lab.UndoLastSourceEdit());
        Assert.Empty(lab.Regions);
        Assert.Equal(LabTruthState.Unknown, lab.Sources.Single().Truth);
    }

    [Fact]
    public async Task PositiveImageRegionCannotThenBecomeNegative()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Image,
            LabVisualDomain.Illustration2D, true);
        Assert.Throws<ArgumentException>(() => lab.AddPositiveRegion(source.Id,
            new NormalizedRect(.2, .2, .3, .3), "candidate", 0, 0));
        lab.AddPositiveRegion(source.Id, new NormalizedRect(.2, .2, .3, .3), "candidate");
        Assert.Throws<InvalidOperationException>(() => lab.MarkImageNegative(source.Id, true));
        Assert.Equal(LabTruthState.Positive, lab.Sources.Single().Truth);
    }

    [Fact]
    public async Task UncertainIsNeverEquivalentToNegative()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Image,
            LabVisualDomain.Unknown, true);
        lab.MarkUncertain(source.Id);
        var reloaded = ValidationLabStore.Open(Project).Sources.Single();
        Assert.Equal(LabTruthState.Uncertain, reloaded.Truth);
        Assert.Equal(LabReviewState.PartiallyReviewed, reloaded.ReviewState);
    }

    [Theory]
    [InlineData("https://x.com/User_1/status/123", "https://x.com/User_1/status/123")]
    [InlineData("https://twitter.com/User_1/status/123?s=20", "https://x.com/User_1/status/123")]
    [InlineData("https://x.com/User_1/status/123/", "https://x.com/User_1/status/123")]
    public void PostUrl_NormalizedWithoutTrackingQuery(string input, string expected)
    {
        Assert.True(XSourceLinks.TryNormalizePostUrl(input, out string actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://x.com/user/status/1")]
    [InlineData("https://x.com.evil.invalid/user/status/1")]
    [InlineData("https://x.com@evil.invalid/user/status/1")]
    [InlineData("https://x.com/home")]
    [InlineData("https://x.com/user/status/invalid")]
    [InlineData("https://x.com:444/user/status/1")]
    [InlineData("https://x.com/user/status/1/extra")]
    public void ArbitraryUrls_AreNotSavedAsXPermalinks(string url) =>
        Assert.False(XSourceLinks.TryNormalizePostUrl(url, out _));

    [Fact]
    public void PostBookmarksOnlyStoreCanonicalLinksAndNotes()
    {
        var lab = ValidationLabStore.Open(Project);
        var first = lab.AddPostBookmark("https://twitter.com/test/status/123?utm_source=foo", "my note");
        var second = lab.AddPostBookmark("https://x.com/test/status/123");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("https://x.com/test/status/123", first.PostUrl);
        Assert.Single(ValidationLabStore.Open(Project).Bookmarks);
        Assert.Throws<ArgumentException>(() => lab.AddPostBookmark("https://evil.invalid/"));
    }

    [Fact]
    public void SearchUrlUsesOrdinaryXWebPagesNotAPI()
    {
        var url = XSourceLinks.SearchUrl("from:sample", "images");
        Assert.Equal("x.com", url.Host);
        Assert.Equal("/search", url.AbsolutePath);
        Assert.Contains("has%3Aimages", url.Query);
        Assert.Equal("https://x.com/sample", XSourceLinks.UserUrl("@sample").ToString().TrimEnd('/'));
        Assert.Throws<ArgumentException>(() => XSourceLinks.UserUrl("bad/path"));
        Assert.Throws<ArgumentOutOfRangeException>(() => XSourceLinks.SearchUrl("x", "unknown"));
    }

    [Fact]
    public async Task CorruptedProjectDoesNotGetSilentlyReset()
    {
        var lab = ValidationLabStore.Open(Project);
        await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Image,
            LabVisualDomain.Photograph, true);
        var backup = File.ReadAllText(Project);
        File.WriteAllText(Project, "{\"SchemaVersion\":99}");
        Assert.Throws<InvalidDataException>(() => ValidationLabStore.Open(Project));
        File.WriteAllText(Project, backup);
        Assert.Single(ValidationLabStore.Open(Project).Sources);
    }

    [Fact]
    public async Task AtomicWriteCreatesBackupOfPreviousSnapshot()
    {
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(MakeFile(), LabMediaKind.Image,
            LabVisualDomain.Photograph, true);
        lab.MarkImageNegative(source.Id, true);
        Assert.True(File.Exists(Project + ".bak"));
        var old = JsonSerializer.Deserialize<ValidationLabDocument>(File.ReadAllText(Project + ".bak"));
        Assert.NotNull(old);
        Assert.Equal(LabTruthState.Unknown, old!.Sources.Single().Truth);
    }
}
