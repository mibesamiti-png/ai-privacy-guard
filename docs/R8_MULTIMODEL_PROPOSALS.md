# R8.0 — Independent full-frame multi-model proposals (research core)

**Status:** experimental Core implementation and synthetic tests. No specialized underwear/garment model weights are provided, installed, trained or measured in this release. The existing image/video/batch UI and OBS streaming pipeline remain unchanged.

## Why the R8 design is independent, not crop-only

R6's actual NudeNet 320n evaluation returned no selected-category boxes for the tested clothed 2D/3D character controls. A second-stage model that only classifies NudeNet boxes would never see those images. R8.0 therefore introduces \`MultiModelProposalAnalyzer.ScanAsync\`, which always passes the **entire original PixelBuffer** independently to every supplied \`IExposureDetector\` (up to eight).

- Model A can return zero regions while a separate, future garment-specialist Model B returns candidate regions.
- Every proposal retains its \`ModelId\`, class, original normalized rectangle and **own model score**. Scores from different models are not calibrated; there is **no cross-model ranking or suppression**.
- Valid proposals from a successful model are retained when another model fails, but \`MultiModelScanResult.IsComplete\` is **false**; \`ModelScanStatus.ErrorCode\` explicitly indicates \`inference_failed\` or \`invalid_output\`, without leaking exception messages or private file paths.
- Malformed boxes, non-finite scores, invalid model IDs and overlong model outputs are rejected. A failed model's partial proposals are discarded.
- Cancellation propagates rather than being disguised as a completed scan.
- Existing consent, manual approval and source-hash review requirements still govern all future UI integration. This new core helper does **not** auto-approve, mask, export or silently delete a candidate.

## What it DOES NOT implement

- No new pretrained model, classifier, fine-tuning pipeline or automatic garment-under-skirt recognition has been integrated.
- No training or accuracy claim for anime, 3D, cosplay or real people.
- No changes to NudeNet's default threshold; raising or lowering it without labeled positives would be unjustified.
- No UI toggle, live camera/OBS integration or production safety guarantee.
- \`IsComplete\` only says each configured model finished with structurally valid results. Zero proposals or a complete scan **never** means "safe for livestream."

## Reproducible verification

Run \`dotnet test tests/PrivacyGuard.Core.Tests/PrivacyGuard.Core.Tests.csproj -c Release\` and review the Windows CI status. Synthetic xUnit tests cover independent specialist detection despite zero NudeNet boxes, per-model score/provenance preservation, inference failure isolation, invalid output atomicity, bounded inputs, duplicate model IDs and cancellation. These test adapters are **stubs**, not an actual garment classifier.

## Next increments

1. **R8.1**: Specify independently annotated, consented adult-positive and adult-negative still/video examples. Include balanced source domains (2D, 3D, photographed/cosplay) and explicit annotation rules for garment visibility and image/video context. Keep all private pixels, coordinates tied to personal media and model credentials **out of public GitHub**. Derive a deidentified benchmark split and dataset provenance.
2. **R8.2**: Train or obtain a suitably licensed specialist detector, document exact model hash, tensor contract, input preprocessing and target classes, and add true model inference tests. The specialist must be able to **propose its own boxes on a full frame**, not depend solely on NudeNet's ROIs.
3. **R8.3**: Independently evaluate precision, recall, false negatives, domain shifts, time per frame and calibration against a held-out labeled set. Use the existing \`scripts/evaluate_detections.py\` only where its box-level annotations apply; also add event-level video metrics.
4. **R8.4**: After accuracy and source-data handling are reviewed, integrate explicit opt-in multi-model scan into image/video/batch review queues. All new proposals begin **Pending** and retain provenance. Live OBS remains separate and experimental until additional hardening and hardware tests.

Security: this core service does not read the network, files or send image data anywhere; the injected detectors retain their own implementation responsibilities. Do not use it to certify live privacy protection.
