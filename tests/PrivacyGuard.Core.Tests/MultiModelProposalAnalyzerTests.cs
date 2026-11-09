using PrivacyGuard.Core;
using Xunit;

namespace PrivacyGuard.Core.Tests;

public sealed class MultiModelProposalAnalyzerTests
{
    private sealed class StubDetector : IExposureDetector
    {
        private readonly Func<CancellationToken, Task<IReadOnlyList<DetectionCandidate>>> _run;
        public string ModelId { get; }
        public int Calls { get; private set; }

        public StubDetector(string modelId,
            Func<CancellationToken, Task<IReadOnlyList<DetectionCandidate>>> run)
        {
            ModelId = modelId;
            _run = run;
        }

        public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(
            PixelBuffer image, CancellationToken cancellationToken)
        {
            Calls++;
            return _run(cancellationToken);
        }

        public void Dispose() { }
    }

    private static PixelBuffer Frame() => new(4, 4, new byte[4 * 4 * 4]);
    private static DetectionCandidate Proposal(
        string category = "UNDERWEAR_VISIBLE_REVIEW_CANDIDATE",
        double score = .8, NormalizedRect? rect = null) =>
        new(category, score, rect ?? new NormalizedRect(.1, .2, .3, .4));

    private static StubDetector Returns(string id, params DetectionCandidate[] candidates) =>
        new(id, _ => Task.FromResult<IReadOnlyList<DetectionCandidate>>(candidates));

    [Fact]
    public async Task SpecialistScansFullFrameEvenWhenPrimaryFindsNothing()
    {
        var primary = Returns("nudenet", []);
        var specialist = Returns("specialist", Proposal());
        var result = await MultiModelProposalAnalyzer.ScanAsync(
            Frame(), [primary, specialist]);

        Assert.True(result.IsComplete);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, specialist.Calls);
        var item = Assert.Single(result.Proposals);
        Assert.Equal("specialist", item.ModelId);
        Assert.Equal("UNDERWEAR_VISIBLE_REVIEW_CANDIDATE", item.Candidate.Category);
        Assert.Equal(0, result.Models[0].ProposalCount);
        Assert.Equal(1, result.Models[1].ProposalCount);
    }

    [Fact]
    public async Task OverlappingModelsKeepIndependentScoresAndProvenance()
    {
        var result = await MultiModelProposalAnalyzer.ScanAsync(Frame(),
        [
            Returns("nudenet", Proposal("COVERED", .95)),
            Returns("specialist", Proposal("GARMENT_REVIEW", .20))
        ]);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Proposals.Count);
        Assert.Equal(["nudenet", "specialist"],
            result.Proposals.Select(p => p.ModelId).ToArray());
        Assert.Equal(.95, result.Proposals[0].Candidate.Score);
        Assert.Equal(.20, result.Proposals[1].Candidate.Score);
    }

    [Fact]
    public async Task InferenceFailureIsExplicitButDoesNotSuppressOtherModel()
    {
        var broken = new StubDetector("broken",
            _ => Task.FromException<IReadOnlyList<DetectionCandidate>>(
                new IOException("C:\\private\\do-not-leak.jpg")));
        var specialist = Returns("specialist", Proposal());
        var result = await MultiModelProposalAnalyzer.ScanAsync(
            Frame(), [broken, specialist]);

        Assert.False(result.IsComplete);
        Assert.Equal("inference_failed", result.Models[0].ErrorCode);
        Assert.False(result.Models[0].Completed);
        Assert.True(result.Models[1].Completed);
        Assert.Single(result.Proposals);
        Assert.DoesNotContain("private", string.Join("|",
            result.Models.Select(m => m.ErrorCode ?? "")));
    }

    [Fact]
    public async Task InvalidProposalDiscardsAllCandidatesFromThatModel()
    {
        var invalid = Returns("bad",
            Proposal("VALID"),
            Proposal("INVALID", double.NaN));
        var good = Returns("good", Proposal("GOOD"));
        var result = await MultiModelProposalAnalyzer.ScanAsync(
            Frame(), [invalid, good]);

        Assert.False(result.IsComplete);
        Assert.Equal("invalid_output", result.Models[0].ErrorCode);
        Assert.Equal(0, result.Models[0].ProposalCount);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal("good", proposal.ModelId);
    }

    [Fact]
    public async Task OutOfBoundsCandidateIsRejectedWithoutClamping()
    {
        var bad = Returns("bad", Proposal(rect: new NormalizedRect(.8, .8, .4, .4)));
        var result = await MultiModelProposalAnalyzer.ScanAsync(Frame(), [bad]);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Proposals);
        Assert.Equal("invalid_output", Assert.Single(result.Models).ErrorCode);
    }

    [Fact]
    public async Task DuplicateIdsAreRejectedBeforeAnyModelRuns()
    {
        var first = Returns("duplicate", Proposal());
        var second = Returns("duplicate", Proposal());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            MultiModelProposalAnalyzer.ScanAsync(Frame(), [first, second]));

        Assert.Equal(0, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task CancellationPropagatesRatherThanReportingSuccess()
    {
        using var cts = new CancellationTokenSource();
        var first = new StubDetector("slow", token =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DetectionCandidate>>([]);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MultiModelProposalAnalyzer.ScanAsync(Frame(), [first], cts.Token));
    }

    [Fact]
    public async Task EmptyOrExcessModelsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MultiModelProposalAnalyzer.ScanAsync(Frame(), Array.Empty<IExposureDetector>()));
        var tooMany = Enumerable.Range(0, MultiModelProposalAnalyzer.MaxModels + 1)
            .Select(i => (IExposureDetector)Returns($"model{i}")).ToArray();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MultiModelProposalAnalyzer.ScanAsync(Frame(), tooMany));
    }
}
