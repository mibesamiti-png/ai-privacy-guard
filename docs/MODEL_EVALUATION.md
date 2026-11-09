# Model evaluation — independent ground truth vs reviewer feedback

## 1. Two fundamentally different signals

- **Review feedback CSV** from app: reviewer approval/rejection/deferral counts for visible model suggestions. Useful for identifying noisy categories and prioritizing manual inspection. It **cannot** estimate false negatives or detector recall, and must not be described as tested precision.
- **Offline labeled benchmark** with *independently annotated* ground truth for every evaluated image/frame, including frames without exposure. Use `scripts/evaluate_detections.py` to obtain TP / FP / FN and precision / recall / F1 at a specified score and match-IoU threshold.

Never equate an accepted candidate with ground truth in a production safety claim; the reviewer may also be wrong.

## 2. Run the synthetic fixture (no personal content)

```powershell
python .\scripts\evaluate_detections.py .\samples\eval_fixture_synthetic.json --score 0.5 --iou 0.5 --out .\samples\my_evaluation.json
python -m unittest discover -s .\tests\python -v
```

Expected synthetic aggregate: TP=1, FP=2, FN=1, precision=1/3, recall=1/2, F1=0.4. These numbers are **not real model performance**.

## 3. Input JSON format

```json
{
  "schemaVersion": 1,
  "images": [
    {
      "id": "frame_anonymized_001",
      "truth": [{"category": "TEST_REGION", "box": [0.10, 0.20, 0.30, 0.20]}],
      "predictions": [{"category": "TEST_REGION", "box": [0.10, 0.20, 0.30, 0.20], "score": 0.9}]
    }
  ]
}
```

Boxes are `[x,y,width,height]` normalized to 0..1. Predictions contain uncalibrated raw score 0..1. Match is one-to-one per category and per image, greedy by descending score at user-specified IoU threshold. No source media is read; file IDs may be anonymous. Keep real content and annotations private and do not commit them to public repositories.

## 4. What still needs to happen

- Obtain licensed, representative evaluation data with explicit annotation rules and enough negatives, lighting conditions, outfits, movements, image qualities and camera angles.
- Evaluate the *actual* trained target model, not a generic object detector or synthetic fixture.
- Compare performance per category, pose and image type and investigate false negatives carefully.
- Record model hash, architecture, input dimensions, preprocessing mode, dataset version, evaluator parameters and run date.
- For livestream use, measure temporal miss events, latency and fail-closed behavior independently. This offline evaluator does **not** prove livestream safety.
