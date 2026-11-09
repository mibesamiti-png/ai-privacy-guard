#!/usr/bin/env python3
"""Offline structural checks. Does not replace .NET build or GUI tests."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
issues: list[str] = []

for path in (*root.rglob('*.xaml'), *root.rglob('*.csproj'), *root.rglob('*.slnx')):
    try:
        ET.parse(path)
    except ET.ParseError as exc:
        issues.append(f'XML parse failed: {path.relative_to(root)}: {exc}')

for path in root.rglob('*.csproj'):
    for node in ET.parse(path).iter():
        if node.tag.endswith('ProjectReference'):
            target = (path.parent / node.attrib['Include'].replace('\\', '/')).resolve()
            if not target.is_file():
                issues.append(f'Missing reference: {path.name}: {target}')

xaml = (root / 'src/PrivacyGuard.App/MainWindow.xaml').read_text(encoding='utf-8')
code = '\n'.join(path.read_text(encoding='utf-8') for path in (root / 'src/PrivacyGuard.App').rglob('*.cs'))
handlers = set(re.findall(
    r'\b(?:Click|SelectionChanged|ValueChanged|SizeChanged|MouseLeftButtonDown|'
    r'MouseMove|MouseLeftButtonUp|LostMouseCapture|Closing)="([A-Za-z_][\w]*)"', xaml
))
for handler in handlers:
    if not re.search(r'\b' + re.escape(handler) + r'\s*\(', code):
        issues.append(f'Missing XAML handler: {handler}')

for name in ['LoadModelButton', 'DetectButton', 'ModelProbeButton', 'ExportProbeButton', 'CropImageView', 'PreviewCanvas', 'VideoFrameCanvas', 'VideoCandidateList', 'VideoTimelineSlider', 'BatchExportList', 'BatchCompareButton', 'BatchCompareBeforeImage', 'BatchCompareAfterImage', 'BatchMarkComparedButton']:
    if f'x:Name="{name}"' not in xaml:
        issues.append(f'Missing XAML named control: {name}')

try:
    m = json.loads((root / 'samples/model_manifest.example.json').read_text(encoding='utf-8'))
    if (m.get('schemaVersion') != 1 or not isinstance(m.get('classes'), dict) or
            m.get('preprocessing') not in {'stretch_nearest', 'letterbox_bilinear'}):
        issues.append('Invalid manifest example schema')
except (json.JSONDecodeError, OSError) as exc:
    issues.append(f'Manifest template parse failed: {exc}')

if issues:
    print('\n'.join('ERROR: ' + issue for issue in issues))
    sys.exit(1)
print(f'PASS: XML files, references, {len(handlers)} WPF handlers, model manifest example')
print('WARNING: C# compilation, tests, and native ONNX inference were NOT checked.')
