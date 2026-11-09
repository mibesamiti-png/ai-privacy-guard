# R6 real-model visual check (reproducible, no private media)

This test goes beyond the earlier solid-color ONNX wiring smoke: the **actual
pinned 320n ONNX** is invoked through the project's `OnnxExposureDetector`
against eight deterministic synthetic illustrations of adults and, if its source is reachable, a cropped CC0 photograph of an adult marble sculpture.

To review the results, download the GitHub Actions artifact
`R6-NudeNet-Visual-Test-Data-And-Results` from the green run. It contains:

- `inputs/*.png`: exactly the images tested (eight original non-explicit illustrations and optionally a public-domain adult classical sculpture photograph).
- `fixtures.json`: image descriptions, SHA-256, output dimensions and source metadata.
- `report/results.json`: actual detections, confidence, normalized and pixel bounding boxes, inference timing.
- `report/*_annotated.bmp`: detected region rectangles on source images.
- `report/*_blackout_preview.bmp`: **simulation** applying a black mask to all detected candidates.
- `report/README.md`: per-image detection counts and limitations.

**Important:** the eight drawings are simplistic, and the optional adult marble statue is art rather than a live person; neither is an actual underwear-leak ground truth. An all-zero detection result is possible and is
not by itself a success/failure of a nudity detection algorithm. Actual
false-negative and false-positive measurements require a separately
consented and labeled real-world evaluation set. None of those images should
be committed to a public repository.

## Reproduction

The GitHub Windows workflow installs Pillow, runs:

```powershell
python scripts/make_nudenet_eval_fixtures.py --output dist/nudenet-visual-eval
./scripts/run-nudenet-visual-eval.ps1
```

The script retrieves the third-party model into temporary storage, checks the
pinned SHA-256, performs inference, then deletes model weights. The generated
PNG/BGRA are produced from the **same pixels**; raw BGRA is discarded after inference.

The optional photo is from Wikimedia Commons, photographed by Mustang Joe, CC0: https://commons.wikimedia.org/wiki/File:Michelangelo%27s_David_(8292252317).jpg . If its CDN download fails, the script records the omission and runs the synthetic eight. The photo is cropped for evaluation and source attribution is retained in fixtures.json.\n\nThe visual step is `continue-on-error` because the upstream model host or
PyPI may be temporarily unavailable. A green overall workflow is NOT enough:
verify the `NUDENET_VISUAL_EVAL_PASS` message and result JSON. No input
image, crop or inference is sent to a hosted AI service. External network is
only used to retrieve the public model and Pillow, not upload images.

## Test goals

- Verify inference on non-flat 480×640 images with zero-square padding.
- Ensure each emitted bounding box is within the original image.
- Generate inspectable original, bounding-box and masked images for review.
- Catch regressions and unexpected false positives on clothed illustrations.
- Do not claim model recall or real broadcast protection without actual labeled tests.
