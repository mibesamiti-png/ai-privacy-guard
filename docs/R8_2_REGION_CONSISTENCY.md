# R8.2 — Garment-label conflict review with an independent crop model

**Status:** experimental Python research only. The Windows WPF app and OBS bridge are unchanged; no trained exposure detector or validated safety system exists.

## Problem found in R8.1

On one non-human 256x256 flat geometric dress silhouette, Grounding DINO Tiny emitted **11** proposals and assigned several conflicting garment labels to overlapping locations, including skirt/shorts/underwear/dress/swimsuit. These are not 11 true positive objects. OWLv2 emitted zero candidates at its tested research threshold; neither result implies actual underwear visibility detection.

We need to retain all candidates, identify overlapping label contradictions, and perform **independent** image-context comparison without overriding the original detection or reporting a safety decision.

## Architecture

1. **Candidate proposals:** R8.1 JSON from the dedicated offline zero-shot research runner, or an entirely non-human synthetic sample.
2. **Region grouping:** IoU-connected regions at threshold 0.65. Each group's representative crop is the highest-scored box; the original labels and proposal count remain intact. Adjacent disjoint regions remain separate. NaN, infinite, out-of-bounds, oversized and malformed inputs are rejected.
3. **Context crop:** 20% padding around the representative region, bounded by the input image.
4. **Independent image-text comparison:** LAION CLIP ViT-B/32 safetensors checkpoint, MIT-tagged by upstream, compared to six fixed English research phrases: dress, skirt, shorts, swimsuit, underwear, geometric non-clothing shape.
5. **Output:** JSON-only region IDs, normalized coordinates, conflicting candidate labels, prompt-relative ranking, inference time and **review_required=true, safety_status=unknown**. It deliberately does *not* suppress the detector, set approved/rejected, or mark a frame safe.

Official research checkpoint: https://huggingface.co/laion/CLIP-ViT-B-32-laion2B-s34B-b79K

**Important:** CLIP image/text ranking is not a calibrated probability, learned garment occlusion detector or positive exposure classifier. Prompts are research hypotheses, not ground-truth categories. This checkpoint is about 605MB and **not yet a lightweight production model**. License tags, dependencies and model-use restrictions must be reviewed before redistribution/deployment. Never use as production privacy/surveillance protection.

## Synthetic GitHub verification

The separate research workflow \`.github/workflows/r8-2-region-research.yml\` downloads the safetensors checkpoint at runtime, runs CPU inference on **one original abstract flat garment silhouette**, validates the JSON schema, and publishes metadata only. No reference photos, human imagery, weights or source media are checked in or uploaded to the artifacts.

Run locally with a consented adult private image and its previously generated R8.1 JSON, never uploading user pixels:

\`\`\`powershell
python scripts/r8_2_region_reviewer.py --image "C:\private\approved.png" --proposals "C:\private\r8_result.json" --out "C:\private\r8_region_report.json"
\`\`\`

Synthetic run:

\`\`\`powershell
python scripts/r8_2_region_reviewer.py --synthetic --out r8_synthetic_region_report.json
\`\`\`

Python requirements for offline model research: Python 3.11, CPU PyTorch 2.6.0 and torchvision 0.21.0, Transformers 4.51.3, Pillow 11.2.1, SciPy 1.16.3 and safetensors >=0.5. Use \`--offline\` only when model is already cached. First run may download about 605MB of weight data; no automatic download in the WPF app. Model snapshot is not yet hash-pinned and reported inference times are for a single runner only.

## Meaning of success

- **Unit tests**: clustering semantics, contradictory labels, no automatic safety approval, bounded crops, invalid outputs, empty candidates, and budget.
- **Real model smoke**: the model weights loaded and an image crop was passed through real CPU inference. A few prompt rankings do not establish correct labels.
- **Not verified**: precision/recall for under-skirt exposure, 2D/3D/photo accuracy, consented positive corpora, video recall, model quantization, GPU timing or live OBS safety.

## Next: R8.3

Independently annotate licensed adult and non-human positive/negative cases with a stable dataset schema and leakage-safe domain split (2D/3D/photo); establish quantitative confusion matrices and false-negative metrics. Then train/benchmark a genuinely small specialist, export ONNX only when validated, and integrate only behind an opt-in pending-review UI with explicit model provenance. **No private test pixels in public GitHub.**
