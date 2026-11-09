# R6 — Adult apparel, cosplay, 2D/3D cross-domain debug (verified)

**Verified Windows GitHub Actions**: [run #42707282951](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42707282951), with `NUDENET_THRESHOLD_SWEEP_PASS` and `NUDENET_REALMEDIA_EVAL_PASS`. This ran the actual pinned 320n NudeNet ONNX model in the project's C# `OnnxExposureDetector`, not a simulated classifier.

## Exact test scope

- **19/19 remote images fetched successfully**, including 7 earlier clothing/control media and 12 newly added adult lingerie/swimwear/dress and Japanese Comiket C105 professional bunny costume photos.
- Actual image inference at 0.25 plus a threshold comparison at **0.10, 0.25, 0.40** (19 × 3 = 57 inference calls in the sweep).
- An additional initial 0.25 image inference pass generated the normal report and abstract detection-box maps.
- Existing 109 core and 7 OBS xUnit tests passed, all Windows workflow jobs completed.
- All model weights and source photographs were in ephemeral CI storage only; source pixels and photographs were **not** included in the artifact.
- The test images contain **no independently annotated under-skirt positive examples**, no verified privacy incidents, and no undercover/covert filming.

## Measured results

| Score threshold | Images containing a detection | Region count | Lingerie-reference photos with any region |
|---|---:|---:|---:|
| 0.10 | 5/19 | 6 | 2/5 |
| **0.25 (shipped default)** | **2/19** | **2** | **1/5** |
| 0.40 | 1/19 | 1 | 0/5 |

These are **NOT** real positive recall, precision or sensitivity metrics for accidental exposure. The provisional lingerie reference label comes from the external image description, not a box-level exposure annotation.

### Which images triggered the model?

| Sample | Score threshold | Model category | Score | Interpretation |
|---|---:|---|---:|---|
| Public adult swimwear runway | 0.10 / 0.25 / 0.40 | `FEMALE_GENITALIA_COVERED` | 0.66582 | Model recognized a covered-area candidate on ordinary swimsuit, not a panty-shot |
| DareToDesire lingerie 0211 | 0.10 / 0.25 | `FEMALE_GENITALIA_COVERED` | 0.27043 | Weak detection on lingerie photo |
| Jackie Martinez black-and-white lingerie | 0.10 only | `BUTTOCKS_EXPOSED` | 0.11758 | Low-confidence covered/uncovered-area ambiguity |
| Jackie Martinez blue bikini | 0.10 only | `BUTTOCKS_EXPOSED` / `FEMALE_GENITALIA_COVERED` | 0.19051 / 0.12516 | Two low-confidence detections in swimsuit |
| Jackie Martinez black dress | 0.10 only | `FEMALE_BREAST_EXPOSED` | 0.22566 | **Potential false positive** on conventionally dressed body, needs visual/manual check |

Other adult Raiden and Yor cosplays, 2D Yor/Ningguang, 3D Raiden/Ningguang and the C105 bunny convention photo produced **zero selected-category boxes** at all three tested thresholds.

## Diagnosis

1. NudeNet's selected 18-class taxonomy has no `UNDERWEAR_GARMENT_VISIBLE` or `UNDER_SKIRT_EXPOSURE` class. Its `GENITALIA_COVERED` is an anatomy/coverage proposal, not proof that a pair of panties is seen.
2. Reducing threshold 0.25 → 0.10 added just one more lingerie-referenced image, but also an ordinary-dress `FEMALE_BREAST_EXPOSED` proposal. Lowering the threshold globally increases false-positive risk; **not recommended as an unqualified default change**.
3. Generic nude-anatomy detector zero predictions on clothed anime/3D art cannot establish missed panties without genuine, consented adult positive references.
4. Five lingerie photos were mainly dominated by the same two photographers/model sessions, so their distribution cannot support independent population-wide recall estimates.
5. Actual user privacy protection requires **consented positive examples with precise ROI annotations**, and a specialized garment/pose context model or multi-stage classifier; the existing live OBS feature remains experimental.

## Downloadable test evidence

GitHub Actions artifact: **`R6-Lingerie-Fashion-Cosplay-Threshold-Test`** on [run #42707282951](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42707282951).

Contains:
- `fixtures.json` — 19 original photo/game/art source links, sizes, SHA-256 and provisional garment labels.
- `report/results.json` — default threshold 0.25 classes, scores and pixel/normalized boxes.
- `report/threshold_sweep.json` — full per-image 0.10/0.25/0.40 boxes and categories.
- `report/threshold_sweep.csv` — spreadsheet-friendly results.
- `geometry_maps/*.png` — blank-background coordinate diagrams; no identifiable source imagery.
- `report/README.md` — test notes.

Reproduction: `./scripts/run-nudenet-public-media-eval.ps1` on Windows with .NET 10, Python and FFmpeg. The script downloads the model with a pinned SHA-256, deletes third-party images and weights after inference, and never commits them to GitHub.
