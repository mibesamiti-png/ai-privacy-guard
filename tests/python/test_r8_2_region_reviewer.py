import math
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from r8_2_region_reviewer import (  # noqa: E402
    LABELS, analyze, crop_region, group_proposals, iou, rank_labels,
    validated_box, synthetic_input,
)


def proposal(category, box=None, score=.7):
    return {"category": category, "score": score,
            "box": box or [.1, .1, .7, .7]}


class FakeImage:
    def __init__(self, width=256, height=256):
        self.width, self.height = width, height
        self.crops = []
    def crop(self, xyxy):
        self.crops.append(xyxy)
        return self


def fake_pipeline(patch, candidate_labels):
    return [{"label": label, "score": .25 + i * .05}
            for i, label in enumerate(candidate_labels)]


class RegionConsistencyTests(unittest.TestCase):
    def test_overlap_iou(self):
        a = (.1, .2, .4, .5)
        self.assertEqual(1, iou(a, a))
        self.assertEqual(0, iou(a, (.8, .8, .1, .1)))

    def test_transitively_grouped_overlaps(self):
        proposals = [
            proposal("a skirt", [.10, .10, .5, .5]),
            proposal("a dress", [.12, .1, .5, .5]),
            proposal("underwear", [.14, .1, .5, .5]),
        ]
        groups = group_proposals(proposals, min_iou=.7)
        self.assertEqual(1, len(groups))
        self.assertEqual(3, groups[0]["proposal_count"])
        self.assertTrue(groups[0]["conflicting_labels"])
        self.assertEqual(3, len(groups[0]["labels"]))

    def test_disjoint_candidates_not_collapsed(self):
        groups = group_proposals([
            proposal("a skirt", [.1, .1, .2, .2]),
            proposal("a dress", [.6, .6, .2, .2]),
        ])
        self.assertEqual(2, len(groups))

    def test_malformed_inputs_fail_closed_not_silently_normalized(self):
        invalid = [
            proposal("a skirt", [.8, .8, .3, .3]),
            proposal("a dress", [0, 0, float("nan"), .5]),
            proposal("a dress", [0, 0, 0, .5]),
            proposal("a dress", [0, 0, .5, .5], score=float("inf")),
            proposal("bad\nlabel"),
        ]
        for entry in invalid:
            with self.subTest(entry=entry), self.assertRaises(ValueError):
                group_proposals([entry])
        with self.assertRaises(ValueError):
            group_proposals([proposal("x")] * 201)

    def test_rejects_unbounded_groups(self):
        values = []
        for y in range(6):
            for x in range(6):
                values.append(proposal("a dress", [x / 6, y / 6, .10, .10]))
        with self.assertRaises(ValueError):
            group_proposals(values)

    def test_scores_not_classified_as_probabilities(self):
        ranked = rank_labels(fake_pipeline(None, list(LABELS)))
        self.assertEqual(len(LABELS), len(ranked))
        self.assertEqual("relative_score", next(iter(ranked[0].keys() - {"label"})))
        self.assertGreater(ranked[0]["relative_score"], ranked[-1]["relative_score"])
        with self.assertRaises(ValueError):
            rank_labels([{"label": LABELS[0], "score": 0.8}] * len(LABELS))
        with self.assertRaises(ValueError):
            rank_labels([{"label": label, "score": math.nan} for label in LABELS])

    def test_crop_includes_context_and_is_bounded(self):
        img = FakeImage()
        crop_region(img, [.2, .2, .4, .4])
        left, top, right, bottom = img.crops[0]
        self.assertLess(left, .2 * img.width)
        self.assertGreater(right, .6 * img.width)
        self.assertTrue(0 <= left < right <= img.width)
        self.assertTrue(0 <= top < bottom <= img.height)

    def test_manual_review_remains_required_even_when_labels_seem_agree(self):
        img = FakeImage()
        result = analyze(img, [
            proposal("a dress", [.1, .1, .5, .5]),
            proposal("a dress", [.11, .1, .5, .5]),
        ], fake_pipeline)
        region = result[0]
        self.assertEqual(1, len(result))
        self.assertFalse(region["candidate_conflict"])
        self.assertTrue(region["review_required"])
        self.assertEqual("unknown", region["safety_status"])
        self.assertEqual(2, region["proposal_count"])

    def test_zero_candidates_no_claim_of_safety(self):
        self.assertEqual([], analyze(FakeImage(), [], fake_pipeline))

    def test_synthetic_fixture_contains_only_abstract_garment(self):
        image, proposals = synthetic_input()
        self.assertEqual((256, 256), image.size)
        self.assertEqual(3, len(proposals))
        self.assertEqual(1, len(group_proposals(proposals)))


if __name__ == "__main__":
    unittest.main()
