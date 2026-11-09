import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from evaluate_detections import evaluate, iou, load_fixture  # noqa: E402


class EvaluationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.images = load_fixture(ROOT / 'samples' / 'eval_fixture_synthetic.json')

    def test_fixture_counts_and_rates(self):
        report = evaluate(self.images)
        self.assertEqual({'tp': 1, 'fp': 2, 'fn': 1},
                         {k: report['overall'][k] for k in ('tp', 'fp', 'fn')})
        self.assertAlmostEqual(1/3, report['overall']['precision'])
        self.assertAlmostEqual(.5, report['overall']['recall'])
        self.assertAlmostEqual(.4, report['overall']['f1'])

    def test_iou_edge_cases(self):
        self.assertEqual(0, iou((0, 0, .1, .1), (.8, .8, .1, .1)))
        self.assertEqual(1, iou((.3, .3, .2, .2), (.3, .3, .2, .2)))

    def test_prediction_score_filter(self):
        report = evaluate(self.images, min_score=.95)
        self.assertEqual(0, report['overall']['tp'])
        self.assertEqual(2, report['overall']['fn'])
        self.assertEqual(0, report['overall']['fp'])

    def test_rejects_invalid_fixture(self):
        broken = {"schemaVersion":1,"images":[{"id":"x","truth":[],
             "predictions":[{"category":"TEST", "box":[0,0,1.5,1],"score":1}]}]}
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'bad.json'
            path.write_text(json.dumps(broken), encoding='utf-8')
            with self.assertRaises(ValueError): load_fixture(path)

    def test_single_prediction_cannot_match_twice(self):
        one = [{"id":"one", "truth":[
            {"category":"TEST", "box":[.1,.1,.3,.3]},
            {"category":"TEST", "box":[.1,.1,.3,.3]}], "predictions":[
            {"category":"TEST", "box":[.1,.1,.3,.3], "score":.9}]}]
        report = evaluate(one)
        self.assertEqual(1, report['overall']['tp'])
        self.assertEqual(1, report['overall']['fn'])


if __name__ == '__main__':
    unittest.main()
