# Experimental external model contract – R2.0

**Important:** No model is provided. A generic human detection ONNX file is not an intimate-exposure detector. This contract enables local inference for models expressly designed/evaluated for intended categories, **not reliable livestream protection**.

## Load a model

1. Place a compatible `.onnx` model next to its JSON manifest (it may be in a subdirectory beneath the manifest).
2. Compute SHA256 in Windows PowerShell: `Get-FileHash .\your-model.onnx -Algorithm SHA256`.
3. Copy `samples/model_manifest.example.json`, set `modelFile`, `modelSha256`, tensor names, dimensions, coordinate convention and real class labels.
4. Click **AIモデル定義を開く**, select the JSON, then **AIで候補を抽出** on an open image.
5. Check every resulting candidate manually in the review queue. Approval is required to mask.

## Supported model signature

- Input: `float32` RGB `[1,3,H,W]`, values `[0,1]` and manifest-selected `stretch_nearest` (legacy, stretched nearest neighbor) or `letterbox_bilinear` (aspect-preserving resize, centered 114/255 padding). The choice MUST match model training/export preprocessing.
- Output: `float32` `[N,6]` or `[1,N,6]`; rows `[x1, y1, x2, y2, score, classId]`. This is **postprocessed detection output**, not raw YOLO heads.
- Coordinates: either `xyxy_input_pixels` (input dimensions) or `xyxy_normalized` (unit interval). R2.1 transforms boxes back into original image coordinates, accounting for letterbox padding and actual integer resize size.
- Class map: integer class ID to category identifier (e.g. `UNDERWEAR_EXPOSURE`). Mapping labels does **not** change what the model actually recognizes. Labels must reflect validated model training/behavior.
- Candidate normalization: invalid/unknown classes, NaN, low-confidence or empty boxes are discarded; clipped to image bounds; same-class IoU NMS performed; at most 200 candidates per run.
- The score is an uncalibrated model confidence, **not** a probability that a specific body part is exposed.

## Privacy and trust

- Model is selected explicitly, loaded locally, SHA256 verified, and requires manifest-local model file (no external URLs).
- ONNX models are complex input files; load only from trusted sources. SHA256 verifies the file matches the manifest, not its origin or safety.
- No network transfer of source images. No crops are persisted automatically. Region metadata and original file path are saved in the app's local SQLite database.
- The model is not automatically reloaded on startup; model selection is deliberate.
- ONNX Runtime 1.31.0 NuGet package is downloaded during first restore/build. No model is downloaded.
- Do not use this experimental build for real-time incident prevention; video/OBS is not implemented.

## R2.1 caveats

- Manifest key `preprocessing` is optional for old configs and defaults to `stretch_nearest`. Choose `letterbox_bilinear` only for models trained/exported with matching resize/padding conventions.
- Other preprocessing layouts, RGB mean/std normalization, variable input dimensions, center/width/height outputs, raw YOLO heads and masks are **not** supported. A matching model and validation are required.
- Confidence is uncalibrated. Review-feedback counts are not false-negative measurements.
- Use `docs/MODEL_EVALUATION.md` for the independent offline evaluation harness.

## R2.2 diagnostics

See `docs/MODEL_DIAGNOSTICS.md`. An offline SHA256 validator and synthetic runtime probe were added, but neither verifies real detection accuracy. Do not represent a probe PASS as model certification.
