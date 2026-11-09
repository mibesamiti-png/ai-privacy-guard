# Model diagnostics: two levels of verification

This application intentionally separates **file integrity**, **runtime interoperability**, and **real detection accuracy**. They are not interchangeable.

## Level 1: Offline manifest/hash check

```powershell
python .\scripts\validate_model_manifest.py .\models\your_manifest.json
```

Checks JSON fields, allowed preprocessing and output coordinate names, class map, file location and SHA256. The file might be invalid ONNX despite a successful result. No user images are read.

## Level 2: ONNX synthetic smoke probe

1. Start the Windows app and select a trusted, locally stored compatible manifest via **AIモデル定義を開く**.
2. Click **モデル接続診断**. Two synthetic RGB gradients (160×96 and 96×160) pass through the selected model and the app's normal decode pipeline.
3. Review status and click **診断JSONを保存** if desired.
4. Click **AI解析を停止** to request cancellation. Native ONNX operations may not cancel immediately; a model that hangs should be treated as untrusted/unfit for deployment.

The JSON schema contains model ID, expected SHA256, UTC time, status, two input shapes, per-case latency and resulting candidate count (or failure type). It contains no source media, original image names/paths, or saved crops.

`Passed` does not mean any exposure was detected, that classes are correct, that the confidence score is calibrated, or that output is safe for a livestream. A poorly trained model returning zero candidates will still pass this interface-level test.

## Level 3: Ground-truth evaluation — separate

See `docs/MODEL_EVALUATION.md` and `scripts/evaluate_detections.py` for synthetic evaluation-harness tests. Evaluate a real model with properly consented and annotated positive/negative images, including different garments, lighting, backgrounds and poses, and record FP/FN and false negative cases. No such trained model or benchmark is supplied.

## Threats and operational limits

- Loading an ONNX model executes complex native code. Use trusted, license-compliant models only; a matching hash does not establish trustworthiness.
- The model does not send imagery to external servers in this application. NuGet restore/Windows build may download packages.
- The synthetic probe does not measure real GPU speed or live-video throughput; this release uses the CPU ONNX package.
- Do not use this release as a protection filter for an active stream.
