# R2.2 — Model interoperability diagnostics / R3 timeline foundation

Date: 2026-11-09. Source changes only; Windows app build and native ONNX execution **not run here**.

## New

- `src/PrivacyGuard.Core/ModelProbe.cs`: runs a selected detector against two **synthetic, in-memory** gradients (wide 160×96, tall 96×160). Reports per-case result, milliseconds and candidate count. Catches runtime/shape/decoder failures and serializes **only exception type**, not sensitive paths, pixels or raw error text.
- Model UI buttons: `モデル接続診断` and `診断JSONを保存`; both disabled until appropriate state. Cancels via existing AI stop button. Model changes reset old diagnostic report.
- `scripts/validate_model_manifest.py`: dependency-free offline manifest and model-byte SHA256 checks. Its `ByteIntegrityPassed` does **not** attest to ONNX validity or model accuracy.
- `src/PrivacyGuard.Core/VideoEventGrouping.cs`: pure-library R3 groundwork for grouping timestamped candidate rectangles into intervals by category, track ID and IoU, without conflating frame numbers with PTS. Not yet connected to video decoding or UI.
- 11 new C# xUnit tests authored (5 diagnostics, 6 event-grouping), **not executed** in this environment.
- 6 new Python tests for manifest preflight, all executed and passing (11 Python tests total including prior R2.1 evaluator tests).
- Windows CI artifact version advanced to R2.2; CI has not been run from this conversation.

## Safety boundaries

- A successful synthetic probe only means the model accepted the input and returned well-formed outputs **for these synthetic frames**. It proves nothing about revealing actual exposure or preventing leaks in broadcasts.
- Native ONNX execution in this session is not verified. Compatible, trustworthy model weights must be supplied by the user; there are **no real ONNX weights** in the ZIP.
- All existing review/manual image masking behavior is retained. No user media is bundled.
- `VideoEventGrouping` is only the event aggregator: no FFmpeg ingestion, no masking timeline, no R3 video tab functionality yet.

## Follow-up / blocker

1. On Windows, run `scripts/build.ps1`, inspect compiler errors and unit-test failures; check UI interactions on a sample non-person synthetic image.
2. With a trusted compatible ONNX model, run `scripts/validate_model_manifest.py` and UI synthetic probe. Record actual native result separately from this authored source.
3. Obtain independently labeled, consented benchmark samples for real accuracy assessment. Report precision/recall only from that benchmark.
4. Move to R3 FFmpeg-backed image-by-PTS decode and timeline review after Windows build is green.
