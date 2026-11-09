#!/usr/bin/env python3
"""R8.1 opt-in local zero-shot garment candidate comparison.

This is an experimental research runner, NOT an under-skirt exposure detector.
Only explicit CLI invocation downloads weights; input image pixels are not uploaded.
Public CI uses non-human geometric synthetic input only.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import time
from pathlib import Path
from typing import Any

MODELS = {
    "grounding-dino-tiny": "IDEA-Research/grounding-dino-tiny",
    "owlv2-base-ensemble": "google/owlv2-base-patch16-ensemble",
}
PROMPTS = (
    "a skirt",
    "a dress",
    "a swimsuit",
    "a pair of shorts",
    "underwear",
)
MAX_IMAGE_PIXELS = 16_000_000
MAX_IMAGES = 16
MAX_BOXES_PER_IMAGE = 200


def validate_threshold(value: float) -> float:
    if not math.isfinite(value) or value < 0 or value > 1:
        raise ValueError("threshold must be finite and between 0 and 1")
    return value


def normalize_prediction(prediction: dict[str, Any], width: int, height: int) -> dict:
    """Strictly normalize a Transformers pipeline box; no silent clipping."""
    if width <= 0 or height <= 0:
        raise ValueError("invalid image dimensions")
    if not isinstance(prediction, dict) or not isinstance(prediction.get("box"), dict):
        raise ValueError("invalid pipeline prediction")
    box = prediction["box"]
    try:
        x1, y1, x2, y2 = (float(box[k]) for k in ("xmin", "ymin", "xmax", "ymax"))
        score = float(prediction["score"])
    except (KeyError, ValueError, TypeError) as exc:
        raise ValueError("invalid prediction fields") from exc
    label = prediction.get("label")
    if (not isinstance(label, str) or not (1 <= len(label) <= 120)
            or any(ord(ch) < 32 for ch in label)):
        raise ValueError("invalid candidate label")
    if not all(map(math.isfinite, (x1, y1, x2, y2, score))):
        raise ValueError("nonfinite model output")
    if (x1 < 0 or y1 < 0 or x2 > width or y2 > height or
            x1 >= x2 or y1 >= y2 or not 0 <= score <= 1):
        raise ValueError("invalid model bounding box or score")
    return {
        "category": label,
        "score": round(score, 7),
        "box": [round(x1 / width, 7), round(y1 / height, 7),
                round((x2-x1) / width, 7), round((y2-y1) / height, 7)],
    }


def normalize_results(raw: Any, width: int, height: int) -> list[dict]:
    if not isinstance(raw, list) or len(raw) > MAX_BOXES_PER_IMAGE:
        raise ValueError("model returned an invalid or unbounded prediction list")
    return [normalize_prediction(item, width, height) for item in raw]


def synthetic_garment():
    """Non-human, unlabelled geometrical dress proxy; no sensitivity test."""
    from PIL import Image, ImageDraw
    image = Image.new("RGB", (256, 256), "#e9edf1")
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 209, 255, 255), fill="#d5dbe1")
    draw.polygon([(105, 40), (151, 40), (166, 90), (193, 205),
                  (65, 205), (90, 90)], fill="#3f6ea8")
    draw.line([(105, 40), (151, 40), (166, 90), (193, 205),
               (65, 205), (90, 90), (105, 40)], fill="#273e64", width=4)
    return image


def open_local_images(paths: list[str], use_synthetic: bool) -> list:
    from PIL import Image, UnidentifiedImageError
    Image.MAX_IMAGE_PIXELS = MAX_IMAGE_PIXELS
    if use_synthetic:
        if paths:
            raise ValueError("--synthetic cannot be combined with --input")
        return [synthetic_garment()]
    if not paths or len(paths) > MAX_IMAGES:
        raise ValueError("provide 1 to 16 local image files or --synthetic")
    inputs = []
    for name in paths:
        path = Path(name)
        if path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".webp"):
            raise ValueError("only PNG/JPEG/WEBP local inputs supported")
        try:
            with Image.open(path) as source:
                if source.width * source.height > MAX_IMAGE_PIXELS:
                    raise ValueError("image exceeds pixel limit")
                image = source.convert("RGB")
        except (UnidentifiedImageError, OSError) as exc:
            raise ValueError("unable to decode an input image") from exc
        inputs.append(image)
    return inputs


def compare(
    images: list, model_keys: list[str], threshold: float,
    pipeline_factory: Any
) -> dict:
    validate_threshold(threshold)
    if not images or len(images) > MAX_IMAGES:
        raise ValueError("image count outside bounds")
    if not model_keys or len(model_keys) != len(set(model_keys)):
        raise ValueError("model list must be non-empty and unique")
    for key in model_keys:
        if key not in MODELS:
            raise ValueError("unknown model key")
    reports = []
    for key in model_keys:
        model_id = MODELS[key]
        load_start = time.perf_counter()
        detector = pipeline_factory(model_id)
        load_ms = round((time.perf_counter() - load_start) * 1000, 2)
        samples = []
        for index, image in enumerate(images):
            if image.width * image.height > MAX_IMAGE_PIXELS:
                raise ValueError("image exceeds pixel limit")
            start = time.perf_counter()
            raw = detector(image, candidate_labels=list(PROMPTS), threshold=threshold)
            inference_ms = round((time.perf_counter() - start) * 1000, 2)
            candidates = normalize_results(raw, image.width, image.height)
            samples.append({
                "id": f"sample_{index+1:03d}",
                "width": image.width, "height": image.height,
                "inference_ms": inference_ms,
                "candidates": candidates,
            })
        reports.append({"model": model_id, "load_ms": load_ms, "samples": samples})
    return {
        "schemaVersion": 1,
        "evaluation_type": "unlabeled_zero_shot_garment_probe",
        "threshold": threshold,
        "prompts": list(PROMPTS),
        "models": reports,
        "ground_truth_available": False,
        "privacy_safety_verified": False,
        "warning": (
            "Candidate counts and model scores are not precision/recall. "
            "Under-skirt exposure sensitivity, anime and 3D accuracy are unverified."
        ),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--models", nargs="+", choices=sorted(MODELS), required=True)
    parser.add_argument("--input", nargs="*", default=[])
    parser.add_argument("--synthetic", action="store_true")
    parser.add_argument("--threshold", type=float, default=0.15)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--offline", action="store_true",
                        help="Require cached weights; never access the network")
    args = parser.parse_args()
    validate_threshold(args.threshold)
    if args.offline:
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"

    images = open_local_images(args.input, args.synthetic)
    from transformers import pipeline

    def make_pipeline(model_id: str):
        # Hub downloads model weights only; source images stay local.
        # safetensors avoids untrusted pickle weight deserialization.
        return pipeline("zero-shot-object-detection", model=model_id,
                        device=-1, model_kwargs={"use_safetensors": True,
                                                 "trust_remote_code": False})

    result = compare(images, args.models, args.threshold, make_pipeline)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    if any(Path(x).resolve() == args.out.resolve() for x in args.input):
        raise ValueError("output must not overwrite source image")
    args.out.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                        encoding="utf-8")
    # No source paths or image pixels in logs.
    print("R8_ZERO_SHOT_COMPARE_PASS models=" + str(len(result["models"])) +
          " samples=" + str(len(images)) +
          " ground_truth=false")


if __name__ == "__main__":
    main()
