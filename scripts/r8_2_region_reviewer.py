#!/usr/bin/env python3
"""R8.2 research-only: group overlapping R8.1 boxes and compare their image crops.

The second image-text model returns uncalibrated relative label scores.
It DOES NOT identify under-skirt exposure, certify safety, or overwrite proposals.
No people images or model weights are checked into GitHub.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import time
from pathlib import Path

MODEL_ID = "laion/CLIP-ViT-B-32-laion2B-s34B-b79K"
LABELS = (
    "an illustration of a dress",
    "an illustration of a skirt",
    "an illustration of a pair of shorts",
    "an illustration of a swimsuit",
    "an illustration of underwear",
    "a geometric shape that is not clothing",
)
MAX_GROUPS = 32
MAX_BOXES = 200
MAX_PIXELS = 16_000_000


def validated_box(raw: object) -> tuple[float, float, float, float]:
    if not isinstance(raw, list) or len(raw) != 4:
        raise ValueError("box must contain [x,y,width,height]")
    if any(type(x) not in (int, float) or not math.isfinite(x) for x in raw):
        raise ValueError("nonfinite normalized box")
    x, y, width, height = map(float, raw)
    if (min(x, y) < 0 or width <= 0 or height <= 0 or
            x + width > 1.00000001 or y + height > 1.00000001):
        raise ValueError("out-of-frame box")
    return (x, y, width, height)


def iou(a: tuple, b: tuple) -> float:
    left, top = max(a[0], b[0]), max(a[1], b[1])
    right, bottom = min(a[0] + a[2], b[0] + b[2]), min(a[1] + a[3], b[1] + b[3])
    overlap = max(0.0, right - left) * max(0.0, bottom - top)
    union = a[2] * a[3] + b[2] * b[3] - overlap
    return overlap / union if union > 0 else 0.0


def group_proposals(raw: list[dict], min_iou: float = 0.65) -> list[dict]:
    """IoU-connected components; label conflicts are surfaced, not suppressed."""
    if not (0 < min_iou <= 1) or not math.isfinite(min_iou):
        raise ValueError("invalid cluster IoU")
    if not isinstance(raw, list) or len(raw) > MAX_BOXES:
        raise ValueError("too many proposals")
    proposals = []
    for item in raw:
        if not isinstance(item, dict):
            raise ValueError("proposal must be an object")
        label = item.get("category")
        score = item.get("score")
        if not isinstance(label, str) or len(label) not in range(1, 121) or any(ord(ch) < 32 for ch in label):
            raise ValueError("invalid category")
        if type(score) not in (int, float) or not math.isfinite(score) or not 0 <= score <= 1:
            raise ValueError("invalid score")
        proposals.append({"category": label, "score": float(score),
                          "box": validated_box(item.get("box"))})
    parent = list(range(len(proposals)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(len(proposals)):
        for j in range(i + 1, len(proposals)):
            if iou(proposals[i]["box"], proposals[j]["box"]) >= min_iou:
                a, b = root(i), root(j)
                parent[b] = a

    groups = {}
    for index, item in enumerate(proposals):
        groups.setdefault(root(index), []).append(item)
    result = []
    for items in groups.values():
        top = max(items, key=lambda x: x["score"])
        result.append({
            "box": list(top["box"]),  # one representative crop; never union-expand endlessly
            "proposal_count": len(items),
            "labels": sorted(set(x["category"] for x in items)),
            "conflicting_labels": len(set(x["category"] for x in items)) > 1,
        })
    if len(result) > MAX_GROUPS:
        raise ValueError("too many isolated regions to review at once")
    return sorted(result, key=lambda x: (x["box"][1], x["box"][0]))


def crop_region(image, box: list[float], padding: float = 0.2):
    """Return the padded candidate crop. CLIP alone does not know if it is exposed."""
    x, y, w, h = validated_box(box)
    if image.width * image.height > MAX_PIXELS:
        raise ValueError("image too large")
    left = max(0, math.floor((x - w * padding) * image.width))
    top = max(0, math.floor((y - h * padding) * image.height))
    right = min(image.width, math.ceil((x + w * (1 + padding)) * image.width))
    bottom = min(image.height, math.ceil((y + h * (1 + padding)) * image.height))
    if right - left < 2 or bottom - top < 2:
        raise ValueError("candidate crop too small")
    return image.crop((left, top, right, bottom))


def rank_labels(rows: list[dict]) -> list[dict]:
    """Pipeline scores are prompt-relative and MUST NOT be called calibrated probabilities."""
    if not isinstance(rows, list) or len(rows) != len(LABELS):
        raise ValueError("incomplete model rank list")
    seen = set()
    clean = []
    for row in rows:
        label, score = row.get("label"), row.get("score")
        if label not in LABELS or label in seen:
            raise ValueError("missing or repeated classifier label")
        if type(score) not in (int, float) or not math.isfinite(score) or not 0 <= score <= 1:
            raise ValueError("invalid classifier score")
        seen.add(label)
        clean.append({"label": label, "relative_score": round(float(score), 7)})
    return sorted(clean, key=lambda x: x["relative_score"], reverse=True)


def analyze(image, proposals: list[dict], classify, max_regions: int = MAX_GROUPS) -> list[dict]:
    if max_regions < 1 or max_regions > MAX_GROUPS:
        raise ValueError("invalid review budget")
    groups = group_proposals(proposals)
    if len(groups) > max_regions:
        raise ValueError("review region budget exceeded")
    findings = []
    for index, group in enumerate(groups):
        patch = crop_region(image, group["box"])
        start = time.perf_counter()
        scores = rank_labels(classify(patch, candidate_labels=list(LABELS)))
        elapsed = round((time.perf_counter() - start) * 1000, 2)
        findings.append({
            "region_id": f"region_{index+1:03d}",
            "proposal_box": group["box"],
            "proposal_count": group["proposal_count"],
            "original_candidate_labels": group["labels"],
            "candidate_conflict": group["conflicting_labels"],
            "relative_label_ranking": scores,
            "inference_ms": elapsed,
            "review_required": True,
            "safety_status": "unknown",
        })
    return findings


def load_r8_proposals(path: Path, model_name: str, sample_index: int = 0) -> list[dict]:
    doc = json.loads(path.read_text(encoding="utf-8"))
    if doc.get("schemaVersion") != 1 or doc.get("evaluation_type") != "unlabeled_zero_shot_garment_probe":
        raise ValueError("not an R8.1 candidate report")
    models = [m for m in doc.get("models", []) if m.get("model") == model_name]
    if len(models) != 1 or not isinstance(models[0].get("samples"), list):
        raise ValueError("source model missing or ambiguous")
    samples = models[0]["samples"]
    if not (0 <= sample_index < len(samples)):
        raise ValueError("sample index out of range")
    return samples[sample_index]["candidates"]


def synthetic_input():
    from r8_zero_shot_compare import synthetic_garment
    image = synthetic_garment()
    proposals = [
        {"category": "a skirt", "score": 0.8, "box": [.2, .16, .62, .65]},
        {"category": "a dress", "score": 0.7, "box": [.21, .17, .60, .64]},
        {"category": "underwear", "score": 0.25, "box": [.22, .18, .60, .63]},
    ]
    return image, proposals


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--synthetic", action="store_true",
                        help="Only permitted mode in shared CI; no human images")
    parser.add_argument("--image", type=Path,
                        help="Local consented input, never uploaded by this script")
    parser.add_argument("--proposals", type=Path,
                        help="R8.1 JSON report for the local input")
    parser.add_argument("--model-name", default="IDEA-Research/grounding-dino-tiny")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    if args.synthetic:
        if args.image or args.proposals:
            parser.error("--synthetic cannot be combined with real inputs")
        image, proposals = synthetic_input()
    else:
        if args.image is None or args.proposals is None:
            parser.error("provide both --image and --proposals, or --synthetic")
        from PIL import Image
        Image.MAX_IMAGE_PIXELS = MAX_PIXELS
        if args.image.suffix.lower() not in {".png", ".jpg", ".jpeg", ".webp"}:
            raise ValueError("unsupported image type")
        with Image.open(args.image) as src:
            if src.width * src.height > MAX_PIXELS:
                raise ValueError("image too large")
            image = src.convert("RGB")
        proposals = load_r8_proposals(args.proposals, args.model_name)
    if args.out.resolve() in {p.resolve() for p in (args.image, args.proposals) if p is not None}:
        raise ValueError("do not overwrite source inputs")
    if args.offline:
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"

    from transformers import pipeline
    started = time.perf_counter()
    classifier = pipeline("zero-shot-image-classification", model=MODEL_ID,
                          device=-1, trust_remote_code=False,
                          model_kwargs={"use_safetensors": True})
    load_ms = round((time.perf_counter() - started) * 1000, 2)
    regions = analyze(image, proposals, classifier)
    report = {
        "schemaVersion": 1, "evaluation_type": "r8_2_unlabeled_region_review",
        "model": MODEL_ID, "load_ms": load_ms, "sample": "sample_001",
        "regions": regions, "ground_truth_available": False,
        "safety_verified": False,
        "warning": "All outputs are uncalibrated research suggestions, not validated garment labels or safety decisions.",
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("R8_REGION_REVIEW_REAL_MODEL_PASS regions=" + str(len(regions)) +
          " no_ground_truth=true")


if __name__ == "__main__":
    main()
