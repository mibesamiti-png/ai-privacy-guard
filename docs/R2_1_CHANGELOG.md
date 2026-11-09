# R2.1 changelog — model geometry and quality instrumentation

Base: R2.0 experimental ONNX adapter, R1.1 image review UI.

## Added

- Manifest-controlled `preprocessing`: `stretch_nearest` (legacy behavior) or `letterbox_bilinear` (preserve aspect ratio, bilinear scaling and 114/255 padding).
- Explicit reversible model-to-source coordinate transform including actual resized pixel dimensions and the left/top padding offset. Detections fully inside padding are discarded.
- Early ONNX output tensor metadata validation: [N,6] or [1,N,6]. Does not handle raw YOLO heads.
- Uncalibrated model score display in review queue.
- AI review-feedback CSV: per model and per category, counts of approved, rejected, deferred, pending suggestions and mean raw model score. **This is not measured precision/recall**. The CSV contains no original file path, media or crop pixels.
- Independent offline box-evaluation CLI `scripts/evaluate_detections.py`, using separately annotated truth and predicted boxes; measures precision/recall/F1 for a given synthetic or authorized dataset.
- 8 new C# source tests (preprocessing and feedback), 5 runnable Python evaluation tests and CI step.

## Preserved

- Images, zoom, manually edited regions, mask rendering, history DB; no schema migration required.
- Original image is not overwritten by default; candidate crops are not automatically written to disk.
- The existing external model contract is intentionally strict; no pretrained weights are included.

## Important behavior

- Choose preprocessing according to the actual model training/export procedure. Switching preprocessing arbitrarily can **decrease** accuracy. Existing manifests without the new property default to `stretch_nearest` for compatibility.
- Model scores are not probabilities. Feedback ratios on reviewed suggestions are **not** detector recall, since misses do not enter the queue. Recall requires independent image-by-image ground-truth annotations.
- Live broadcasting, video and folder batching are still planned, not implemented.

## Verification

- Python evaluator: 5 tests executed and passed.
- XML/XAML, references and handler wiring: static check passed, 25 handlers.
- C# compile, xUnit, WPF runtime, native ONNX: not executed (no .NET SDK or Windows runtime in this environment).
