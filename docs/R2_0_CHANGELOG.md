# R2.0 – ONNX adapter foundation (2026-11-09)

## Code added

- `PrivacyGuard.AI`: CPU ONNX Runtime adapter (no included model).
- `PrivacyGuard.Core/AIContracts.cs`: strict external model manifest, SHA256 verification, tensor preprocessor, [N,6]/[1,N,6] decoder, category-aware nonmax suppression, normalized result contract.
- Model picker, asynchronous inference, cancellation request and persistence into existing R1.1 review queue.
- Atomic multi-candidate SQLite inserts; same-model 0.8-IoU duplicate suppression on rerun.
- Core tests for preprocessing, decoding, NMS, invalid inputs, manifest path traversal.

## Limits

- No exposure detection model shipped, no claim of tested detection accuracy.
- CPU-only inference is implemented as experimental source. GPU/WinML requires later evaluation.
- Only compatible float32 NCHW input and predecoded float32 6-column detections supported.
- Detection inference can be cancelled, but synchronous native ONNX execution may finish before cancellation takes effect.
- No Windows .NET SDK in authoring environment: code was not compiled or run there.
- Existing R1.1 WPF UI and image masking remain unchanged except the model controls.

## Next

1. Run Windows CI to restore NuGet, compile the complete solution and execute core tests.
2. Acquire an appropriately licensed, category-capable ONNX model; verify exact input/output names, shape, class mapping, training provenance and relevant accuracy before use.
3. Evaluate inference speed and failure cases with permitted, privacy-safe test media.
4. Improve UX (batch triage, import/export model manifests, selection persistence) once real engine passes tests.
