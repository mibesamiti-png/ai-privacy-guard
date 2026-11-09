namespace PrivacyGuard.Core;

/// <summary>
/// One unmodified detection proposal with its originating model. Scores from
/// different models are not calibrated and must not be compared as probabilities.
/// </summary>
public sealed record ModelProposal(string ModelId, DetectionCandidate Candidate);

/// <summary>
/// Completed means inference returned valid proposals, NOT that an image is safe.
/// ErrorCode intentionally avoids model exception text (which can contain paths).
/// </summary>
public sealed record ModelScanStatus(
    string ModelId, bool Completed, int ProposalCount, string? ErrorCode = null);

public sealed record MultiModelScanResult(
    IReadOnlyList<ModelProposal> Proposals,
    IReadOnlyList<ModelScanStatus> Models)
{
    public bool IsComplete => Models.Count > 0 && Models.All(model => model.Completed);
}

/// <summary>
/// R8 experimental, OFFLINE-ONLY independent full-frame model runner.
/// Each detector sees the full frame, including when NudeNet has zero detections.
/// This collects review proposals, NOT automatic approval, masking or live safety.
/// The UI and OBS pipelines are deliberately not changed by this utility.
/// </summary>
public static class MultiModelProposalAnalyzer
{
    public const int MaxModels = 8;
    public const int MaxProposalsPerModel = 2_000;

    public static async Task<MultiModelScanResult> ScanAsync(
        PixelBuffer image, IReadOnlyList<IExposureDetector> detectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(detectors);
        if (detectors.Count is < 1 or > MaxModels)
            throw new ArgumentOutOfRangeException(nameof(detectors),
                $"Expected 1 to {MaxModels} independently supplied detectors.");

        // Take a snapshot and check every ID before running any inference.
        var models = detectors.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (IExposureDetector? detector in models)
        {
            if (detector is null || !IsValidModelId(detector.ModelId) ||
                !ids.Add(detector.ModelId))
                throw new ArgumentException("Model IDs must be unique and valid.", nameof(detectors));
        }

        var proposals = new List<ModelProposal>();
        var statuses = new List<ModelScanStatus>();
        foreach (var detector in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Independent full-frame pass: do not crop to the earlier model's ROIs.
                var detected = await detector.DetectAsync(image, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (detected is null || detected.Count > MaxProposalsPerModel)
                    throw new InvalidDataException("Invalid proposal collection.");

                // Validate atomically: a broken model must not add partial proposals.
                var validated = new List<ModelProposal>(detected.Count);
                foreach (var candidate in detected)
                {
                    if (!IsValidCandidate(candidate))
                        throw new InvalidDataException("Invalid model proposal.");
                    validated.Add(new ModelProposal(detector.ModelId, candidate));
                }
                proposals.AddRange(validated);
                statuses.Add(new ModelScanStatus(detector.ModelId, true, validated.Count));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                statuses.Add(new ModelScanStatus(detector.ModelId, false, 0, "invalid_output"));
            }
            catch (Exception)
            {
                // Never report a partial multi-model pass as complete. Never include
                // raw exception messages, image data or local filesystem paths.
                cancellationToken.ThrowIfCancellationRequested();
                statuses.Add(new ModelScanStatus(detector.ModelId, false, 0, "inference_failed"));
            }
        }

        // No cross-model NMS or score-based suppression: thresholds and categories
        // differ, and a specialist must not be overridden by NudeNet's score.
        return new MultiModelScanResult(proposals.ToArray(), statuses.ToArray());
    }

    private static bool IsValidModelId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 100 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool IsValidCandidate(DetectionCandidate? candidate)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Category) ||
            candidate.Category.Length > 128 || candidate.Category.Any(char.IsControl) ||
            !double.IsFinite(candidate.Score) || candidate.Score is < 0 or > 1)
            return false;

        var r = candidate.Bounds;
        return double.IsFinite(r.X) && double.IsFinite(r.Y) &&
            double.IsFinite(r.Width) && double.IsFinite(r.Height) &&
            r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
            r.X + r.Width <= 1 && r.Y + r.Height <= 1;
    }
}
