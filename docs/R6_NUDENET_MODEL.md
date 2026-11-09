# R6 — NudeNet 320n ONNX model integration

## Overview

This release implements real detection-model **support** in AI Privacy Guard,
without bundling third-party model weights or any private media.

Previous builds expected already-postprocessed six-column `[N,6]` ONNX
predictions. NudeNet's lightweight 320n YOLOv8 model instead produces a raw
`[1,22,2100]` float tensor: 4 `cx,cy,width,height` channels and 18 category
confidence channels. R6 adds:

- Explicit model output format `nudenet_yolov8_18class`.
- Right/bottom zero-square padding, bilinear RGB/NCHW preprocessing,
  per-pixel normalization and source-coordinate transforms.
- 18-category raw YOLO decoder, score filtering, bounded NMS and normalized
  candidate bounds; only explicitly selected categories are returned.
- A **consent-based** model download in the image tab,
  with pinned SHA-256 and storage under the user's LocalAppData.
- Existing shared `IExposureDetector` usage by image, video-frame analysis,
  video sample scan and folder batch analysis. All detections remain pending
  human review; do not silently approve or export sensitive images.
- Offline reload of an already installed, SHA-256-validated model on app launch.
- An optional real ONNX CPU smoke-test in CI using two **synthetic solid-color**
  frames. It tests wiring and dimensions, NOT accuracy.

## One-time setup in Windows

1. Download the latest green R6 Windows artifact and fully extract it.
2. Launch `AIPrivacyGuard.exe`.
3. Press **露出検出AIを導入（NudeNet）** on the image tab.
4. Review the third-party model and licensing notice. Click Yes to download
   the 320n ONNX model into
   `%LOCALAPPDATA%\AIPrivacyGuard\models\nudenet320\`.
5. Confirm the model status has changed. It can now be used for image, video,
   and batch analysis (via existing buttons). The detection candidates require
   visual review.
6. On subsequent launches, the installed model is validated and loaded
   **offline**, without downloading or uploading photos.

If the network or model host is unavailable, the app reports the problem and
does not install an unverified model. The current code does not support
GPU acceleration; it uses ONNX Runtime CPU inference.

## Default review categories

| ID | Category | Why included |
|---|---|---|
| 0 | FEMALE_GENITALIA_COVERED | Possible skirt/covered-body candidate, very broad |
| 2 | BUTTOCKS_EXPOSED | Exposed buttocks |
| 3 | FEMALE_BREAST_EXPOSED | Exposed breast |
| 4 | FEMALE_GENITALIA_EXPOSED | Exposed genitalia |
| 6 | ANUS_EXPOSED | Exposed anus |
| 14 | MALE_GENITALIA_EXPOSED | Exposed genitalia |
| 17 | BUTTOCKS_COVERED | Possible clothed/underwear candidate, broad |

Notably, the model does **not** have a dedicated
`PANTY_VISIBLE_UNDER_SKIRT` or nipple-only label. It can produce false
positives on ordinary clothing and miss short or small incidents. All classes
are only suggestions pending user confirmation. `FEMALE_BREAST_EXPOSED`
is a larger-region detector and may fail to identify isolated nipple exposure.
The live OBS gate can still display unmasked frames if the model misses an event.

## Model provenance and license review

Model: `320n.onnx` from the NudeNet model family (original
`notAI-tech/NudeNet`, GNU AGPL-3.0; the DeepGHS Hugging Face mirror currently
lists Apache-2.0 metadata). These notices are **not proof** that every model
weight has been relicensed with all required permissions. Do not infer
commercial redistribution rights from a mirror tag. The weights are intentionally
not committed to this public repository or uploaded as GitHub build artifacts.

- Model reference:
  https://github.com/notAI-tech/NudeNet
- Pinned model:
  https://huggingface.co/deepghs/nudenet_onnx/resolve/44956a0587943016350c7815f9e0dec089d45cae/320n.onnx
- Expected SHA-256:
  `c15d8273adad2d0a92f014cc69ab2d6c311a06777a55545f2c4eb46f51911f0f`.

If upstream replaces the model or changes its bytes, the hash check
**rejects** the download rather than silently trusting it.
A private user can separately confirm applicable model-use terms; commercial
distribution or hosting should have a license review first.

## Validation criteria

- Unit tests: output layout, confidence filtering, NMS, class policy,
  original image coordinates, and right/bottom square padding.
- Static/WPF + C# build and previous tests on GitHub Actions.
- Optional ephemeral **actual** ONNX smoke:
  `scripts/smoke-nudenet.ps1` downloads into a temporary directory,
  validates SHA-256, loads `OnnxExposureDetector`, runs two synthetic
  inferences, and deletes weights afterward. This step is marked
  `continue-on-error` to avoid preventing app builds when the external
  model host is unavailable. It must be reported as **unverified** if it fails.
- Manual accuracy metrics (precision, recall, performance over lighting,
  angles, body coverage and 1-frame incidents) are **not evaluated**.
- Never test with private/user media in GitHub Actions or public fixtures.

## R6 follow-up

1. Compare the model on an appropriate, consented, labeled, locally held
   evaluation set before any real stream use.
2. Calibrate score threshold and padding per category; inspect false negatives.
3. Add secure export of model diagnostic counters without personal imagery.
4. Consider a second stage specialized for skirt/underwear visibility since
   NudeNet's covered-body classes do not directly solve that use case.
5. Keep real-time OBS broadcasting in **experimental** mode until fail-closed
   behavior and detection recall are established.

## Verified implementation result (2026-11-10)

- [PR Windows CI #42687318433](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42687318433) **PASS** on final PR head `5b11dd8efe608deab288db9e971fa6bebc994ffd`.
- 109/109 C# core unit tests passed; 7/7 OBS tests passed; both Windows EXE artifacts produced.
- A pinned 320n ONNX model was downloaded to an ephemeral runner, its SHA-256 verified, and two CPU inferences on synthetic 0 and 90 intensity frames completed successfully with the expected zero candidates. `MODEL_SMOKE_PASS` recorded.
- Merged as `69d095ad87fd2e3110237633e7ee40eee21ac5b4` via PR #9.
- Main CI [#42687745033](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42687745033) must be verified separately.
- NOT tested: WPF installer download interaction on a user's PC, actual exposure detection precision/recall, underwear visibility classification, and OBS privacy reliability.
