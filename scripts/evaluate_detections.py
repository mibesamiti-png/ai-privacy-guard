#!/usr/bin/env python3
"""Offline evaluation of predicted boxes against independently labeled ground truth.

Synthetic fixture included; users must supply licensed/authorized evaluation annotations.
This tool never reads or exports image pixels. Review decisions are not valid ground truth
for estimating model recall, because false negatives are absent from review queues.
"""
from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from pathlib import Path


def validate_box(box: object) -> tuple[float, float, float, float]:
    if not isinstance(box, list) or len(box) != 4:
        raise ValueError("box must be [x,y,width,height]")
    if any(not isinstance(v, (int, float)) or not math.isfinite(v) for v in box):
        raise ValueError("box values must be finite numbers")
    x, y, w, h = (float(v) for v in box)
    if min(x, y) < 0 or w <= 0 or h <= 0 or x + w > 1.000001 or y + h > 1.000001:
        raise ValueError("boxes must have positive area within normalized image [0,1]")
    return x, y, w, h


def iou(a: tuple[float, float, float, float], b: tuple[float, float, float, float]) -> float:
    ax, ay, aw, ah = a
    bx, by, bw, bh = b
    ix = max(0.0, min(ax + aw, bx + bw) - max(ax, bx))
    iy = max(0.0, min(ay + ah, by + bh) - max(ay, by))
    intersection = ix * iy
    return intersection / (aw * ah + bw * bh - intersection) if intersection > 0 else 0.0


def _safe_category(value: object) -> str:
    if not isinstance(value, str) or not (1 <= len(value) <= 64) or not all(
        c.isascii() and (c.isalnum() or c == '_') for c in value
    ):
        raise ValueError("invalid category identifier")
    return value


def load_fixture(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(data, dict) or data.get('schemaVersion') != 1 or not isinstance(data.get('images'), list):
        raise ValueError("expected schemaVersion=1 and images array")
    images = data['images']
    if len(images) > 100_000:
        raise ValueError("too many evaluation samples")
    seen: set[str] = set()
    for image in images:
        if not isinstance(image, dict) or not isinstance(image.get('id'), str):
            raise ValueError("each image needs an id string")
        name = image['id']
        if not name or name in seen or len(name) > 200:
            raise ValueError("image IDs must be unique and non-empty")
        seen.add(name)
        for key in ('truth', 'predictions'):
            if not isinstance(image.get(key), list) or len(image[key]) > 10000:
                raise ValueError(f"{key} must be a bounded list")
            for item in image[key]:
                if not isinstance(item, dict):
                    raise ValueError("annotation must be object")
                _safe_category(item.get('category'))
                validate_box(item.get('box'))
                if key == 'predictions':
                    score = item.get('score')
                    if not isinstance(score, (int, float)) or not math.isfinite(score) or not 0 <= score <= 1:
                        raise ValueError("prediction score must be finite [0,1]")
    return images


def evaluate(images: list[dict], min_score: float = .5, match_iou: float = .5) -> dict:
    if (not math.isfinite(min_score) or not math.isfinite(match_iou) or
            not 0 <= min_score <= 1 or not 0 < match_iou <= 1):
        raise ValueError("invalid score / IoU threshold")
    counts = defaultdict(lambda: {'tp': 0, 'fp': 0, 'fn': 0})
    for image in images:
        classes = {a['category'] for a in image['truth']}
        classes.update(a['category'] for a in image['predictions'] if a['score'] >= min_score)
        for cat in classes:
            truth = [validate_box(a['box']) for a in image['truth'] if a['category'] == cat]
            preds = sorted(
                ((a['score'], validate_box(a['box'])) for a in image['predictions']
                 if a['category'] == cat and a['score'] >= min_score),
                reverse=True, key=lambda x: x[0]
            )
            matched: set[int] = set()
            for _, pred_box in preds:
                candidates = [(iou(pred_box, truth[i]), i) for i in range(len(truth)) if i not in matched]
                best_overlap, index = max(candidates, default=(0.0, -1))
                if best_overlap >= match_iou and index >= 0:
                    counts[cat]['tp'] += 1
                    matched.add(index)
                else:
                    counts[cat]['fp'] += 1
            counts[cat]['fn'] += len(truth) - len(matched)
    by_category = {k: _with_rates(counts[k]) for k in sorted(counts)}
    overall = _with_rates({metric: sum(v[metric] for v in counts.values())
                           for metric in ('tp', 'fp', 'fn')})
    return {
        'evaluation_type': 'independently_labeled_boxes',
        'images_evaluated': len(images),
        'min_score': min_score,
        'match_iou': match_iou,
        'overall': overall,
        'by_category': by_category,
        'warning': 'Scores are uncalibrated; this measures only the supplied annotated dataset, not livestream safety.'
    }


def _with_rates(counts: dict) -> dict:
    tp, fp, fn = (counts[k] for k in ('tp', 'fp', 'fn'))
    p = tp / (tp + fp) if tp + fp else 0.0
    r = tp / (tp + fn) if tp + fn else 0.0
    f1 = 2 * p * r / (p + r) if p + r else 0.0
    return {**counts, 'precision': p, 'recall': r, 'f1': f1}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fixture', type=Path, help='JSON annotations with independent ground truth and predictions')
    parser.add_argument('--score', type=float, default=.5)
    parser.add_argument('--iou', type=float, default=.5)
    parser.add_argument('--out', type=Path, help='optional aggregate JSON report')
    args = parser.parse_args()
    report = evaluate(load_fixture(args.fixture), args.score, args.iou)
    out = json.dumps(report, ensure_ascii=False, indent=2)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(out + '\n', encoding='utf-8')
    print(out)


if __name__ == '__main__':
    main()
