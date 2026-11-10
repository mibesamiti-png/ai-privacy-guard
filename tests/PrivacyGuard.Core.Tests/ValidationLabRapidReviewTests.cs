using System.Text.Json;
using PrivacyGuard.Core;
using Xunit;

public sealed class ValidationLabRapidReviewTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apg-rapid-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(_directory, "review.json");

    public ValidationLabRapidReviewTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private async Task<(ValidationLabStore Lab, LabSource Source)> NewImageAsync()
    {
        var filename = Path.Combine(_directory, "original.png");
        File.WriteAllText(filename, "synthetic metadata fixture, not an image");
        var lab = ValidationLabStore.Open(Project);
        var source = await lab.AddLocalSourceAsync(filename, LabMediaKind.Image,
            LabVisualDomain.Illustration2D, rightsConfirmed: true);
        return (lab, source);
    }

    [Fact]
    public async Task AcceptedProposalCreatesHumanGroundTruthButNotFullFrameClearance()
    {
        var (lab, source) = await NewImageAsync();
        var candidate = Guid.NewGuid();
        var assessment = lab.AssessProposal(source.Id, candidate, "onnx:demo",
            "garment", new NormalizedRect(.1, .2, .3, .4), LabProposalVerdict.Correct,
            "garment_visible");
        Assert.Equal(LabProposalVerdict.Correct, assessment.Verdict);
        Assert.NotNull(assessment.TruthRegionId);
        Assert.Equal("garment_visible", lab.Regions.Single().Category);
        Assert.Equal(LabTruthState.Positive, lab.Sources.Single().Truth);
        Assert.Equal(LabReviewState.PartiallyReviewed, lab.Sources.Single().ReviewState);

        lab = ValidationLabStore.Open(Project);
        Assert.Single(lab.Assessments);
        Assert.Single(lab.Regions);
        lab.CompletePositiveImage(source.Id, explicitlyInspected: true);
        Assert.Equal(LabReviewState.FullyReviewed, ValidationLabStore.Open(Project).Sources.Single().ReviewState);
    }

    [Fact]
    public async Task IncorrectProposalDoesNotImplyNegativeOrCoverMissingExposure()
    {
        var (lab, source) = await NewImageAsync();
        var result = lab.AssessProposal(source.Id, Guid.NewGuid(), "onnx:demo",
            "wrong-class", new NormalizedRect(.4, .3, .2, .2), LabProposalVerdict.FalsePositive);
        Assert.Null(result.TruthRegionId);
        Assert.Empty(lab.Regions);
        Assert.Equal(LabTruthState.Unknown, lab.Sources.Single().Truth);
        Assert.Equal(LabReviewState.PartiallyReviewed, lab.Sources.Single().ReviewState);
        Assert.Throws<InvalidOperationException>(() => lab.CompletePositiveImage(source.Id, true));
    }

    [Fact]
    public async Task UndoCorrectAssessmentRestoresSourceAndRemovesTruthAfterRestart()
    {
        var (lab, source) = await NewImageAsync();
        lab.AssessProposal(source.Id, Guid.NewGuid(), "onnx:demo", "dress",
            new NormalizedRect(.1, .1, .5, .5), LabProposalVerdict.Correct, "clothing_visible");
        lab = ValidationLabStore.Open(Project);
        Assert.True(lab.UndoLastSourceEdit());
        Assert.Empty(lab.Regions);
        Assert.Empty(lab.Assessments);
        Assert.Equal(LabTruthState.Unknown, lab.Sources.Single().Truth);
        Assert.Equal(LabReviewState.Unreviewed, lab.Sources.Single().ReviewState);
    }

    [Fact]
    public async Task DuplicateAssessmentNeedsUndoBeforeSecondVote()
    {
        var (lab, source) = await NewImageAsync();
        var id = Guid.NewGuid();
        var bounds = new NormalizedRect(.1, .1, .3, .3);
        lab.AssessProposal(source.Id, id, "onnx:demo", "dress", bounds,
            LabProposalVerdict.Uncertain);
        Assert.Throws<InvalidOperationException>(() => lab.AssessProposal(source.Id, id,
            "onnx:demo", "dress", bounds, LabProposalVerdict.Correct, "garment_visible"));
        Assert.Single(lab.Assessments);
        lab.UndoLastSourceEdit();
        lab.AssessProposal(source.Id, id, "onnx:demo", "dress", bounds,
            LabProposalVerdict.Correct, "garment_visible");
        Assert.Single(lab.Regions);
        Assert.Single(lab.Assessments);
    }

    [Fact]
    public async Task CategoryAndScoreDoNotAutoProveExposure()
    {
        var (lab, source) = await NewImageAsync();
        var rect = new NormalizedRect(.1, .1, .2, .2);
        Assert.Throws<ArgumentException>(() => lab.AssessProposal(source.Id,
            Guid.NewGuid(), "onnx:demo", "underwear", rect, LabProposalVerdict.Correct));
        Assert.Throws<ArgumentException>(() => lab.AssessProposal(source.Id,
            Guid.NewGuid(), "mock", "underwear", rect, LabProposalVerdict.FalsePositive));
        Assert.Throws<ArgumentException>(() => lab.AssessProposal(source.Id,
            Guid.NewGuid(), "onnx:demo", "garment", new NormalizedRect(.9, .9, .8, .2),
            LabProposalVerdict.FalsePositive));
        Assert.Empty(lab.Assessments);
        Assert.Equal(LabTruthState.Unknown, lab.Sources.Single().Truth);
    }

    [Fact]
    public async Task CannotMarkImageNegativeAfterPositiveHumanCandidate()
    {
        var (lab, source) = await NewImageAsync();
        lab.AssessProposal(source.Id, Guid.NewGuid(), "onnx:demo", "dress",
            new NormalizedRect(.1, .1, .2, .2), LabProposalVerdict.Correct, "clothing_visible");
        Assert.Throws<InvalidOperationException>(() => lab.MarkImageNegative(source.Id, true));
        Assert.Equal(LabTruthState.Positive, lab.Sources.Single().Truth);
    }

    [Fact]
    public async Task PositiveImageCannotBeCompletedWithoutExplicitInspection()
    {
        var (lab, source) = await NewImageAsync();
        lab.AddPositiveRegion(source.Id, new NormalizedRect(.1, .1, .2, .2), "garment_visible");
        Assert.Throws<InvalidOperationException>(() => lab.CompletePositiveImage(source.Id, false));
        Assert.Equal(LabReviewState.PartiallyReviewed, lab.Sources.Single().ReviewState);
    }

    [Fact]
    public async Task OldV1ProjectWithNoAssessmentsFieldIsCompatible()
    {
        var (lab, source) = await NewImageAsync();
        string json = File.ReadAllText(Project);
        using var dom = JsonDocument.Parse(json);
        var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dom.RootElement)!;
        obj.Remove("Assessments");
        File.WriteAllText(Project, JsonSerializer.Serialize(obj));
        lab = ValidationLabStore.Open(Project);
        Assert.Single(lab.Sources);
        Assert.Empty(lab.Assessments);
        lab.AssessProposal(source.Id, Guid.NewGuid(), "onnx:demo", "skirt",
            new NormalizedRect(.1, .1, .2, .2), LabProposalVerdict.FalsePositive);
        Assert.Single(ValidationLabStore.Open(Project).Assessments);
    }
}
