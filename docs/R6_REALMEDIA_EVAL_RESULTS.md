# R6 real-media cross-domain test — verified results

Tested in GitHub Windows CI [run #42694286982](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42694286982) (2026-11-09 20:36 UTC), using the pinned NudeNet 320n CPU ONNX model through the project's actual C# `OnnxExposureDetector` pipeline.

- 7 of 7 external images downloaded and decoded.
- Real model invocation, black-frame/box generation and metadata-only export: PASS.
- 1 candidate detection across 7 images.
- **Not a true panty-shot benchmark**: no unauthorized real-person upskirt photos, no minors or age-ambiguous characters, and no verified under-skirt positive images.
- Public media/source pixels and annotated renditions were deleted before GitHub artifact upload. The artifact includes source URLs and rights notices, JSON results and blank geometry maps.

| Input (content) | Actual candidates | Inference ms | Context |
|---|---:|---:|---|
| CC BY adult swimwear runway photo by Max Bosio | 1 | 56.46 | Public fashion event; *not* an accidental under-skirt image |
| Raiden Shogun adult cosplay, CC BY-SA Solomon203 | 0 | 15.89 | Fully clothed; real photo |
| Yor Forger adult cosplay, CC BY-SA Solomon203 | 0 | 14.92 | Fully clothed; real photo |
| Yor Forger official 2D art | 0 | 25.95 | Copyright media, clothed |
| Ningguang official 2D character portrait | 0 | 25.30 | Copyright media, clothed |
| Raiden Shogun actual 3D gameplay screenshot | 0 | 16.09 | Copyright game, clothed |
| Ningguang actual 3D gameplay screenshot | 0 | 16.32 | Copyright game, clothed |

Detected region on the **swimwear** photo:
- Category: `FEMALE_GENITALIA_COVERED`.
- Score: `0.665824`.
- Original inference image: 482×720 px, normalized top-left `(0.599919, 0.729216)`, normalized size `(0.134192, 0.095923)`.
- Pixel rectangle: `x=289, y=525, w=65, h=70`.
- This is **not** evidence of finding involuntary underwear exposure; it merely shows a coarse covered-region detector responding to swimwear.

### What these results mean

- **Confirmed:** the current 18-class NudeNet ONNX can execute on real photography, 2D adult character art, and 3D Genshin screenshots, without crashing or returning invalid rectangles.
- **Unproven:** whether it detects a brief, partially occluded, under-skirt underwear flash in real video or illustrated/game frames. A zero prediction on a fully clothed character is not evidence of a miss or of high accuracy.
- **Important:** the model has no dedicated `UNDER_SKIRT_UNDERWEAR_VISIBLE` category. Sensitivity, false-negative rate, latency at 30/60 FPS, and false-positive rate cannot be estimated from these 7 sources.
- **Next model task:** obtain separately consented **adult** positive examples or legally cleared synthetic adult 3D scenes with explicit box labels, then measure recall and false positives by category. Do not make real live privacy guarantees.

### Inspectable evidence

- Download action artifact `R6-Adult-Fashion-Anime-Genshin-Model-Results` from [CI #42694286982](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42694286982).
- `fixtures.json`: tested image URLs, licensing, pixel sizes and hashes.
- `report/results.json`: confidence scores, category and normalized/pixel rectangles.
- `geometry_maps/*`: abstract box-only diagrams; **source images are intentionally absent**.
- Reproduce with `scripts/run-nudenet-public-media-eval.ps1`. The script downloads media only ephemerally and removes their pixels before publish.
