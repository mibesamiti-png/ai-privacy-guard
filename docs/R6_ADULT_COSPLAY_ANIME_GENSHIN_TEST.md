# R6 realistic distribution evaluation — adult fashion, anime, Genshin

This test probes the **actual pinned 320n NudeNet ONNX model** on public
adult-character reference images, to measure transfer beyond the original
synthetic illustration test. This is not an evaluation of unauthorized or
voyeuristic photography.

**Important absence:** these are *not genuine under-skirt/panty-shot positive
examples*. A true positive/false negative recall estimate requires a separate
**consented adult** positive set with independently labeled exposure boxes.
A public photo of someone in a skirt, a swimsuit runway image, or a
fully clothed adult anime/game character cannot substitute for that.

## Data selection

- Real adult runway swimwear event (Max Bosio, CC BY 2.0).
- Clothed adult Raiden Shogun cosplay (Solomon203, CC BY-SA 4.0).
- Clothed adult Yor Forger cosplay (Solomon203, CC BY-SA 4.0).
- Official 2D adult Yor Forger character illustration (copyright holder).
- 2D Ningguang official-derived character portrait (HoYoverse copyright).
- 3D in-game screenshots: Raiden Shogun and Ningguang (HoYoverse/fan screenshots).

Exact source links, attribution/license details, original dimensions,
content SHA-256 and image availability per run are stored in `fixtures.json`.

No age-ambiguous, child, or child-coded character art is used. No leaked
covert photos, non-consensual images or pornography are sourced.

## Artifact safety

The runner downloads each original image only into **temporary local CI
storage**. The C# model adapter runs ONNX inference on the downloaded image
in BGRA. The process emits the same JSON class/score/normalized geometry as
the earlier visual test. Then the script deletes *every* source image (PNG,
BGRA) and all image-derived annotated/blackout BMP previews before upload.
The final artifact contains only metadata and a diagram of rectangles on a
blank background, not the actual copyrighted/source imagery.

This strategy avoids republishing game art and identifiable cosplayers
without further rights/personality review. The source URL remains in the
JSON, so the original is viewable on its publisher's site.

## Interpretation

The selected test set includes controls and domain shifts. A detection on
swimwear may be a correct broad covered-anatomy *location* but should never
be interpreted as accidental underwear leakage; a detection on fully clothed
anime/game art may be a false positive for the *privacy incident* task.
Zero detections may be correct for the control but **does not prove
the model can detect actual unintended under-skirt incidents**.

The CI stage is marked `continue-on-error` because public hosts can block
automated downloads. Always inspect its own
`NUDENET_REALMEDIA_EVAL_PASS` log line plus the count of sources downloaded;
a green overall workflow is not sufficient for the test to pass.

## Reproduction

```powershell
python scripts/make_nudenet_public_media_eval.py --output dist/nudenet-public-media-eval
./scripts/run-nudenet-public-media-eval.ps1
```

The second command generates fixtures itself, validates the external model's
pinned SHA, runs actual C# ONNX inference, then deletes all source media.
Do not upload copyrighted screenshots to GitHub.
