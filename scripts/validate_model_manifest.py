#!/usr/bin/env python3
"""Offline manifest + model-byte integrity preflight; does NOT load ONNX/infer."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys


def inspect_manifest(manifest_path: str | Path) -> dict:
    p = Path(manifest_path).resolve()
    doc = json.loads(p.read_text(encoding='utf-8'))
    if not isinstance(doc, dict) or doc.get('schemaVersion', 1) != 1:
        raise ValueError('unsupported manifest schema')
    model_id = doc.get('modelId', '')
    if not isinstance(model_id, str) or not re.fullmatch(r'[A-Za-z0-9_.-]{1,100}', model_id):
        raise ValueError('invalid model ID')
    filename = doc.get('modelFile', '')
    if not isinstance(filename, str) or not filename.lower().endswith('.onnx'):
        raise ValueError('modelFile must reference an ONNX file')
    if Path(filename).is_absolute() or '\\' in filename and ':' in filename:
        raise ValueError('modelFile must be relative')
    model = (p.parent / filename).resolve()
    if model == p.parent or p.parent not in model.parents:
        raise ValueError('modelFile must remain inside manifest directory')
    for name in ('inputName', 'outputName'):
        if not isinstance(doc.get(name, {'inputName': 'images', 'outputName': 'detections'}[name]), str) or \
           not doc.get(name, {'inputName': 'images', 'outputName': 'detections'}[name]).strip():
            raise ValueError(f'invalid {name}')
    for name in ('inputWidth', 'inputHeight'):
        value = doc.get(name, 640)
        if isinstance(value, bool) or not isinstance(value, int) or not 32 <= value <= 2048:
            raise ValueError(f'invalid {name}')
    if doc.get('coordinates', 'xyxy_input_pixels') not in ('xyxy_input_pixels', 'xyxy_normalized'):
        raise ValueError('unsupported coordinates')
    if doc.get('preprocessing', 'stretch_nearest') not in ('stretch_nearest', 'letterbox_bilinear'):
        raise ValueError('unsupported preprocessing')
    for name, default, lower_inclusive in (
        ('scoreThreshold', .5, True), ('nmsIouThreshold', .5, False)):
        value = doc.get(name, default)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or \
           not (0 <= value <= 1 if lower_inclusive else 0 < value <= 1):
            raise ValueError(f'invalid {name}')
    classes = doc.get('classes')
    if not isinstance(classes, dict) or not 1 <= len(classes) <= 64:
        raise ValueError('missing or invalid class IDs')
    for id_, label in classes.items():
        if not isinstance(id_, str) or not re.fullmatch(r'[0-9]{1,5}', id_) or int(id_) > 10000 or \
           not isinstance(label, str) or not re.fullmatch(r'[A-Za-z0-9_]{1,64}', label):
            raise ValueError('invalid class mapping')
    expected = doc.get('modelSha256', '')
    if not isinstance(expected, str) or not re.fullmatch(r'[a-fA-F0-9]{64}', expected):
        raise ValueError('missing SHA256')
    h = hashlib.sha256()
    with model.open('rb') as f:
        while chunk := f.read(1024 * 1024):
            h.update(chunk)
    if h.hexdigest().lower() != expected.lower():
        raise ValueError('SHA256 mismatch')
    return {
        'status': 'ByteIntegrityPassed',
        'modelId': model_id,
        'modelSha256': h.hexdigest().upper(),
        'note': 'This checks manifest and bytes only; ONNX graph/inference and exposure accuracy are NOT checked.'
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args(argv)
    try:
        result = inspect_manifest(args.manifest)
    except (ValueError, OSError, UnicodeError, json.JSONDecodeError) as exc:
        print(json.dumps({'status': 'Failed', 'failureType': type(exc).__name__,
                          'note': 'Manifest, file or hash verification failed. No inference performed.'}))
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
