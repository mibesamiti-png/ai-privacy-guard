# R6 adult-apparel positive-proxy and threshold debugging

## Motivation

Previous public photographs of 2D/3D Genshin, clothed cosplay and one adult runway swimsuit rarely produced a positive. However those were **not positive examples of accidental under-skirt underwear visibility**, so a zero result does not measure the miss rate.

This pass increases sample diversity using public, professionally photographed **adult lingerie, bikini and normal clothed fashion** with clear Wikimedia Commons provenance, alongside the existing adult Raiden/Yor cosplay, 2D art and Genshin 3D screenshots. It is a study of *model response to garments*, not confirmation that any picture represents a privacy incident.

## Newly added input groups

- Jackie Martinez: three published lingerie/underwear photos (black, white, B&W); three swimsuit variants (black, blue, leopard bikini); three clothed controls (dress and white outfit).
- DareToDesire: two published lingerie photo sessions, photographer-owned Commons CC BY-SA 4.0.
- Japanese Comiket C105 professionally photographed adult bunny costume: an **ordinary-costume control**. Permission for further redistribution is not assumed.
- Earlier images: seven professional fashion/cosplay, adult 2D Yor & Ningguang, 3D Raiden & Ningguang controls.

Source metadata records photographer/license and links. The image itself remains in the temporary GitHub Actions worker; originals and image-based previews are deleted in a `finally` block before artifacts are published. Do not upload private or identifiable source images to the public GitHub repo.

## Executed inference

The pinned SHA-256 320n NudeNet ONNX is executed through the actual C# `OnnxExposureDetector`, not substituted with a web image caption classifier. Test three confidence thresholds:

- **0.10** — low threshold; increases possible recall and false positives.
- **0.25** — current R6 default.
- **0.40** — stricter; reduces candidate volume.

For each source, output all selected NudeNet categories, confidence scores, original normalized coordinates and count for each threshold. Compare the proportion with any detected region for *visibly lingerie* references, but label that statistic a **proxy detection rate, NOT true privacy-event recall**. A clothing label from filename/Commons is provisional and may not be spatially verified; the same model appears in multiple photos, so observations are not independent.

## What remains unanswered

- This corpus does **not** contain verified accidental underwear glimpses, specifically under-skirt events, and cannot quantify false negatives for that use case.
- No dedicated under-skirt underwear class in NudeNet. A fully clothed-control score may still refer to a normal anatomical covered-region, not a mistaken assertion of an incident.
- Do not reduce the shipping threshold solely based on these figures. Require a separately consented adult positive set, precise annotation boxes, evaluation of per-class precision/recall, and video temporal validation before enabling for live privacy protection.

## Reproducible steps

```powershell
./scripts/run-nudenet-public-media-eval.ps1
```

This script downloads sources ephemerally, tests model integrity, runs the R6 visual scan plus `--threshold-sweep`, generates blank-background box maps, removes all source pixels, and emits:

- `fixtures.json`: per-source URLs, provisional garment labels, pixel sizes, hashes, failed fetches
- `report/results.json`: default model detections (threshold 0.25)
- `report/threshold_sweep.json`: full detection categories and coordinates per threshold
- `report/threshold_sweep.csv`: spreadsheet-friendly summary
- `geometry_maps/*.png`: box-only maps, NO original person/character image content

The external image stage is intentionally `continue-on-error` for network availability; inspect its exact job conclusion and `NUDENET_THRESHOLD_SWEEP_PASS` plus `NUDENET_REALMEDIA_EVAL_PASS` before reporting success.
