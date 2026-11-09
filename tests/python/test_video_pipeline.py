"""Integration test of FFmpeg command strategy. This does NOT compile/run C# WPF."""
from __future__ import annotations
import hashlib
import io
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    Image = None


def run(*args):
    return subprocess.run(args, check=True, capture_output=True).stdout


@unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe') and Image is not None,
                     'FFmpeg, FFprobe and Pillow are required for integration tests')
class VideoPipelineIntegrationTests(unittest.TestCase):
    def test_metadata_frame_and_blackbox_export_preserves_audio(self):
        with tempfile.TemporaryDirectory() as tmp:
            inp, out = Path(tmp) / 'source.mp4', Path(tmp) / 'guarded.mp4'
            run('ffmpeg', '-hide_banner', '-loglevel', 'error',
                '-f', 'lavfi', '-i', 'color=c=white:s=160x120:r=10:d=3',
                '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=44100:duration=3',
                '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-shortest', str(inp))
            source_hash = hashlib.sha256(inp.read_bytes()).hexdigest()
            probe = json.loads(run('ffprobe', '-v', 'error', '-select_streams', 'v:0',
                                   '-show_entries', 'stream=width,height,avg_frame_rate,r_frame_rate:format=duration',
                                   '-of', 'json', str(inp)))
            self.assertEqual(probe['streams'][0]['width'], 160)
            self.assertGreater(float(probe['format']['duration']), 2.9)
            output = run('ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error',
                         '-ss', '1.5', '-i', str(inp), '-map', '0:v:0', '-frames:v', '1',
                         '-an', '-sn', '-vf', 'scale=1280:720:force_original_aspect_ratio=decrease',
                         '-f', 'image2pipe', '-c:v', 'png', 'pipe:1')
            img = Image.open(io.BytesIO(output))
            self.assertEqual(img.size, (960, 720))
            run('ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error', '-y',
                '-i', str(inp), '-map', '0:v:0', '-map', '0:a?',
                '-vf', "drawbox=x=20:y=20:w=60:h=40:color=black:t=fill:enable='between(t,1,2)'",
                '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p',
                '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', str(out))
            self.assertTrue(out.is_file())
            self.assertEqual(hashlib.sha256(inp.read_bytes()).hexdigest(), source_hash)
            streams = json.loads(run('ffprobe', '-v', 'error', '-show_entries',
                                     'stream=codec_type', '-of', 'json', str(out)))['streams']
            self.assertIn('audio', [s['codec_type'] for s in streams])

            def pixel_at(second):
                png = run('ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error',
                          '-ss', str(second), '-i', str(out), '-frames:v', '1',
                          '-f', 'image2pipe', '-c:v', 'png', 'pipe:1')
                return Image.open(io.BytesIO(png)).convert('RGB').getpixel((40, 35))[0]
            self.assertGreater(pixel_at(0.5), 180)
            self.assertLess(pixel_at(1.5), 60)
            self.assertGreater(pixel_at(2.5), 180)


if __name__ == '__main__':
    unittest.main()
