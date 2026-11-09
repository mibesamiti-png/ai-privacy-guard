"""FFmpeg validation for R4.1's reviewed-mask output mechanism, not a WPF integration test."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

FFMPEG = 'ffmpeg'
FFPROBE = 'ffprobe'


class BatchVideoOutputTest(unittest.TestCase):
    def test_reviewed_interval_output_and_audio(self):
        with tempfile.TemporaryDirectory(prefix='apg-r41-') as folder:
            path = Path(folder)
            source = path / 'source.mp4'
            guarded = path / 'source_guarded.mp4'
            def run(args):
                subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               check=True, timeout=25)
            run([FFMPEG, '-nostdin', '-hide_banner', '-loglevel', 'error', '-y',
                 '-f', 'lavfi', '-i', 'testsrc2=size=160x96:rate=12:duration=1.2',
                 '-f', 'lavfi', '-i', 'sine=frequency=500:sample_rate=16000:duration=1.2',
                 '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', str(source)])
            run([FFMPEG, '-nostdin', '-hide_banner', '-loglevel', 'error', '-n',
                 '-i', str(source), '-vf',
                 "drawbox=x=25:y=15:w=45:h=30:color=black:t=fill:enable='gte(t,0.25)*lt(t,0.9)'",
                 '-map', '0:v:0', '-map', '0:a?', '-c:v', 'libx264',
                 '-pix_fmt', 'yuv420p', '-c:a', 'aac', str(guarded)])
            self.assertTrue(source.exists())
            self.assertGreater(guarded.stat().st_size, 0)
            self.assertNotEqual(source.read_bytes(), guarded.read_bytes())
            raw = subprocess.check_output([FFPROBE, '-v', 'error', '-show_entries',
                                           'stream=codec_type', '-of', 'json', str(guarded)], timeout=15)
            streams = {s['codec_type'] for s in json.loads(raw)['streams']}
            self.assertEqual(streams, {'video', 'audio'})
            # Verify a point inside the reviewed box is masked during the interval.
            # A frame before the interval should retain its original colours.
            def pixel_at(seconds, x=44, y=29):
                raw = subprocess.check_output([
                    FFMPEG, '-nostdin', '-hide_banner', '-loglevel', 'error',
                    '-ss', str(seconds), '-i', str(guarded), '-frames:v', '1',
                    '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'], timeout=20)
                self.assertEqual(len(raw), 160 * 96 * 3)
                return raw[(y * 160 + x) * 3:(y * 160 + x) * 3 + 3]
            self.assertLess(max(pixel_at(0.6)), 25)
            self.assertGreater(max(pixel_at(0.08)), 25)
            # The real C# WPF batch job is not run by this test.


if __name__ == '__main__':
    unittest.main()
