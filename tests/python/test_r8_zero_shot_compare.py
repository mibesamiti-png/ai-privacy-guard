import json
import math
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from r8_zero_shot_compare import (  # noqa: E402
    MODELS, PROMPTS, MAX_IMAGE_PIXELS, compare,
    normalize_prediction, normalize_results, validate_threshold,
)


def sample(score=.75, label="a skirt", box=None):
    return {"score": score, "label": label,
            "box": box or {"xmin": 25, "ymin": 20, "xmax": 75, "ymax": 80}}


class ZeroShotGarmentComparisonTests(unittest.TestCase):
    def test_normalized_box_and_score(self):
        item = normalize_prediction(sample(), 100, 100)
        self.assertEqual("a skirt", item["category"])
        self.assertEqual([.25, .2, .5, .6], item["box"])
        self.assertEqual(.75, item["score"])

    def test_invalid_boxes_and_nonfinite_output_are_rejected(self):
        invalid = [
            sample(box={"xmin": -1, "ymin": 2, "xmax": 5, "ymax": 6}),
            sample(box={"xmin": 25, "ymin": 20, "xmax": 101, "ymax": 80}),
            sample(box={"xmin": 25, "ymin": 20, "xmax": 25, "ymax": 80}),
            sample(score=float("nan")),
            sample(score=1.2),
            sample(label="bad\nlabel"),
        ]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(ValueError):
                normalize_prediction(value, 100, 100)

    def test_nan_or_invalid_threshold_rejected(self):
        for value in [math.nan, math.inf, -.1, 1.01]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_threshold(value)

    def test_unbounded_model_output_rejected(self):
        with self.assertRaises(ValueError):
            normalize_results([sample()] * 201, 100, 100)

    def test_two_models_receive_same_full_frame(self):
        frames_seen = []
        constructors = []
        def make_model(model_id):
            constructors.append(model_id)
            def infer(image, candidate_labels, threshold):
                frames_seen.append((model_id, image, candidate_labels, threshold))
                # First model zero proposals, second finds a garment.
                return [] if "grounding" in model_id else [sample()]
            return infer
        image = SimpleNamespace(width=100, height=100)
        result = compare(image_list := [image], list(MODELS), .15, make_model)
        self.assertEqual(2, len(result["models"]))
        self.assertEqual(list(MODELS.values()), constructors)
        self.assertEqual(2, len(frames_seen))
        self.assertIs(image, frames_seen[0][1])
        self.assertIs(image, frames_seen[1][1])
        self.assertEqual(list(PROMPTS), frames_seen[0][2])
        self.assertEqual(0, len(result["models"][0]["samples"][0]["candidates"]))
        self.assertEqual(1, len(result["models"][1]["samples"][0]["candidates"]))
        self.assertFalse(result["privacy_safety_verified"])
        self.assertFalse(result["ground_truth_available"])
        self.assertNotIn("file_path", json.dumps(result))

    def test_duplicate_models_rejected_before_inference(self):
        with self.assertRaises(ValueError):
            compare([SimpleNamespace(width=100, height=100)],
                    ["grounding-dino-tiny", "grounding-dino-tiny"], .15,
                    lambda _: None)

    def test_input_excessive_size_rejected(self):
        image = SimpleNamespace(width=MAX_IMAGE_PIXELS + 1, height=1)
        with self.assertRaises(ValueError):
            compare([image], ["grounding-dino-tiny"], .15, lambda _: None)

    def test_model_error_is_not_mislabeled_as_complete(self):
        def failing(_model_id):
            def infer(_image, candidate_labels, threshold):
                raise RuntimeError("simulated failure")
            return infer
        with self.assertRaisesRegex(RuntimeError, "simulated failure"):
            compare([SimpleNamespace(width=100, height=100)],
                    ["grounding-dino-tiny"], .15, failing)

    def test_report_does_not_include_paths_or_image_pixels(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "private-user-photo.png"
            image = SimpleNamespace(width=100, height=100)
            result = compare([image], ["grounding-dino-tiny"], .15,
                             lambda _: lambda _image, candidate_labels, threshold: [])
            text = json.dumps(result)
            self.assertNotIn(str(source), text)
            self.assertNotIn("data:", text)
            self.assertNotIn("image_bytes", text)


if __name__ == "__main__":
    unittest.main()
