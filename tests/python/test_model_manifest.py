"""No ONNX runtime needed; fake bytes ONLY test integrity validation."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from validate_model_manifest import inspect_manifest


class ManifestValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        model = self.root / 'fixture.onnx'
        model.write_bytes(b'not actually ONNX')
        self.manifest = {
            'schemaVersion': 1, 'modelId': 'demo', 'modelFile': 'fixture.onnx',
            'modelSha256': hashlib.sha256(model.read_bytes()).hexdigest(),
            'inputWidth': 640, 'inputHeight': 640,
            'classes': {'0': 'TEST_REGION'}
        }

    def write(self):
        p = self.root / 'manifest.json'
        p.write_text(json.dumps(self.manifest), encoding='utf-8')
        return p

    def test_hash_and_manifest_accept_valid_fixture_bytes(self):
        got = inspect_manifest(self.write())
        self.assertEqual('ByteIntegrityPassed', got['status'])
        self.assertNotIn('path', got)
        self.assertIn('NOT checked', got['note'])

    def test_reject_modified_file(self):
        path = self.write()
        (self.root / 'fixture.onnx').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            inspect_manifest(path)

    def test_reject_path_escape(self):
        self.manifest['modelFile'] = '../outside.onnx'
        with self.assertRaisesRegex(ValueError, 'inside'):
            inspect_manifest(self.write())

    def test_reject_unsupported_preprocessing(self):
        self.manifest['preprocessing'] = 'raw_yolo'
        with self.assertRaisesRegex(ValueError, 'preprocessing'):
            inspect_manifest(self.write())

    def test_reject_empty_class_map(self):
        self.manifest['classes'] = {}
        with self.assertRaisesRegex(ValueError, 'class'):
            inspect_manifest(self.write())

    def test_reject_invalid_dimensions(self):
        self.manifest['inputWidth'] = -100
        with self.assertRaisesRegex(ValueError, 'inputWidth'):
            inspect_manifest(self.write())


if __name__ == '__main__':
    unittest.main()
