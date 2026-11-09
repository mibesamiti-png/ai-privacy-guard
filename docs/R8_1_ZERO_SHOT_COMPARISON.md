# R8.1 — Opt-in zero-shot garment model comparison

**State:** Research runner and synthetic CI integration. This is NOT yet a trained or validated detection model and does not alter Windows WPF / OBS functionality.

## Background

The R6 NudeNet model lacks an explicit garment-visibility-under-skirt target class. R8.0's independently executed full-frame multi-model Core service allows an additional model to propose candidates even when NudeNet returns no boxes.

R8.1 explores text-conditioned **garment** detection to guide model selection, not to assert accidental exposure detection accuracy.

### Two official research checkpoints (model weights NOT shipped)

| CLI key | Model repo | Published license* | Primary limitations |
|---|---|---|---|
| \`grounding-dino-tiny\` | \`IDEA-Research/grounding-dino-tiny\` | Apache 2.0 on model card | Large research checkpoint; CPU runtime not proven for live |
| \`owlv2-base-ensemble\` | \`google/owlv2-base-patch16-ensemble\` | Apache 2.0 on model card | Large research checkpoint; zero-shot language sensitivity |

*The repo's license tag is not an independent rights review of every file and dependency. Recheck license, model revision, third-party dependencies, weight provenance and permitted distribution before packaging. The experiment downloads files directly from official hosting when explicitly run. It requests safe tensor loading and disables remote custom code.

Official source pages:
- https://huggingface.co/IDEA-Research/grounding-dino-tiny
- https://huggingface.co/google/owlv2-base-patch16-ensemble
- https://huggingface.co/docs/transformers/model_doc/grounding-dino

## CLI and metrics

Run with Python 3.11, separately install CPU PyTorch and:

\`\`\`shell
python -m pip install "transformers==4.51.3" "Pillow==11.2.1" "safetensors>=0.5,<1"
python scripts/r8_zero_shot_compare.py --models grounding-dino-tiny owlv2-base-ensemble --synthetic --out r8_synthetic_results.json
\`\`\`

Optional local, privately held, **authorized adult-safe garment** images:

\`\`\`shell
python scripts/r8_zero_shot_compare.py --models grounding-dino-tiny owlv2-base-ensemble --input "C:\local\approved_clothing.png" --out "C:\local\r8_results.json"
\`\`\`

- Explicit local invocation only. No automatic model installation as part of Windows application launch.
- Models get full images independently; the fixed prompts are **a skirt, a dress, a swimsuit, a pair of shorts, underwear**.
- JSON contains anonymous sample IDs, width/height, each model's load/inference milliseconds, the original query phrase, uncalibrated score and normalized box. No input paths, photo pixels, personally identifying images, hashes or saved crops are produced by this runner.
- The default comparison threshold is **0.15**, research-only; not a tuned safety threshold. A model returning no proposals does not imply a safe image.
- \`--offline\` only uses cached weights and blocks Hub network access.
- Source code/coordinate normalization can be verified by \`python -m unittest discover -s tests/python -v\` without PyTorch/Transformers. Actual model inference requires dependencies and model downloads; do not conflate these checks.

**Reproducibility caveat:** This R8.1 research prototype uses each public model repo's current default revision rather than pinned immutable model hashes; if checkpoint revisions change, re-run or pin and record exact file hashes before meaningful model performance comparisons.

## Experiment CI

\`.github/workflows/r8-zero-shot-research.yml\` runs **actual** zero-shot model inferences on a geometrical non-human dress illustration, separately from the ordinary Windows build. It is triggered on this PR's relevant files (and manual dispatch). The test **does not** expect a candidate to be emitted, because model inference success != correct garment classification. A successful run requires both actual checkpoints to load and return valid structured responses.

Only a JSON report is uploaded for seven days; no model weights or synthetic image pixels are uploaded. If download or environment setup fails, report it as **NOT VERIFIED**, rather than silently substituting mock predictions.

## What comes next

1. Confirm the actual two-model smoke log and record exact inference timings and model snapshots.
2. Use a consented, adult-only, independently labeled positive/negative dataset covering 2D, 3D and real garments, held outside the public GitHub repository; include unobstructed clothing controls and false-positive negatives.
3. Develop and benchmark event/context-specific criteria on that set. A garment noun detector is not necessarily a reliable visibility/occlusion or under-skirt detector.
4. Only after model validation, design explicit opt-in model import and candidate review in the Windows app. Keep pending human review and never claim livestream safety from candidate proposals.

No live broadcasting protection claims. No covert/unauthorized or underage imagery for model evaluation.

## R8.1 verified research execution results (2026-11-10)
- Successful Windows PR CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42721585795 (129 Core xUnit + 7 OBS xUnit + 30 Python tests; build/publish green).
- Successful actual-model CPU research CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42721586938 . Artifact: `R8-ZeroShot-Synthetic-Model-Results` (JSON only; original synthetic image NOT bundled).
- Synthetic image: one flat non-human geometric dress shape, 256×256; prompts: skirt, dress, swimsuit, shorts, underwear; score threshold 0.15.

| Model | Checkpoint load | Inference | Proposals (NOT unique correct objects) |
|---|---:|---:|---:|
| IDEA-Research/grounding-dino-tiny | 10.583 s | 29.816 s | 11 overlapping, differently labeled boxes |
| google/owlv2-base-patch16-ensemble | 25.833 s | 53.253 s | 0 |

Grounding DINO assigned overlapping labels **shorts, underwear, skirt, swimsuit and dress** to the same synthetic region, showing why prompt proposals must not be mistaken for reliable garment recognition. OWLv2's zero at this threshold/image is not evidence of inferior overall sensitivity. Both raw CPU inference times are too slow for the intended realtime use without substantial adaptation, and model revisions are not yet pinned. No human, anime, 3D or positive exposure images were used in this specific experiment. **Precision/recall are not known.**

Troubleshooting (R8.1): 1) GitHub runner-level context is not valid in a job-level `env`, use `github.workspace`. 2) Transformers pipeline `trust_remote_code` must not be duplicated in `model_kwargs`. 3) OWLv2 image processing needs SciPy. All fixed before final green run.
