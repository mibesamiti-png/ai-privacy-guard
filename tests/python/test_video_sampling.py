"""R3.1 FFmpeg and ffprobe command-strategy tests; does not compile C# or run AI."""
import hashlib
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


def call(*args):
    return subprocess.run(args, check=True, capture_output=True).stdout


@unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe'), 'FFmpeg and ffprobe required')
class VideoSamplingTests(unittest.TestCase):
    def test_bgra_stream_is_frame_aligned_and_source_is_unchanged(self):
        with tempfile.TemporaryDirectory() as folder:
            src = Path(folder) / 'test.mp4'
            call('ffmpeg', '-nostdin', '-loglevel', 'error', '-f', 'lavfi', '-i',
                 'testsrc2=s=160x96:r=10:d=3', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', str(src))
            before = hashlib.sha256(src.read_bytes()).hexdigest()
            for fps in (1, 2, 4):
                raw = call('ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error',
                           '-i', str(src), '-map', '0:v:0', '-vf',
                           f'fps={fps}:start_time=0,scale=160:96:flags=fast_bilinear,format=bgra',
                           '-an', '-sn', '-pix_fmt', 'bgra', '-f', 'rawvideo', 'pipe:1')
                framesize = 160 * 96 * 4
                self.assertEqual(len(raw) % framesize, 0)
                self.assertEqual(len(raw) // framesize, 3 * fps)
                self.assertNotEqual(raw[:framesize], raw[framesize:2 * framesize])
            self.assertEqual(before, hashlib.sha256(src.read_bytes()).hexdigest())

    def test_ffprobe_timestamps_are_ordered_and_can_vary(self):
        with tempfile.TemporaryDirectory() as folder:
            src = Path(folder) / 'vfr.mp4'
            call('ffmpeg', '-nostdin', '-loglevel', 'error', '-f', 'lavfi', '-i',
                 'testsrc2=s=120x80:r=12:d=3', '-vf',
                 'select=not(eq(mod(n\\,4)\\,0))', '-fps_mode', 'vfr',
                 '-c:v', 'libx264', '-pix_fmt', 'yuv420p', str(src))
            output = call('ffprobe', '-v', 'error', '-select_streams', 'v:0',
                          '-show_frames', '-show_entries', 'frame=best_effort_timestamp_time',
                          '-of', 'csv=p=0', str(src)).decode('utf-8')
            pts = []
            for line in output.splitlines():
                try: pts.append(float(line.split(',')[0]))
                except ValueError: continue
            self.assertGreater(len(pts), 10)
            self.assertEqual(pts, sorted(pts))
            gaps = [round(b - a, 3) for a, b in zip(pts, pts[1:])]
            self.assertGreater(len(set(gaps)), 1, 'Synthetic VFR sample must have unequal frame gaps')


if __name__ == '__main__':
    unittest.main()
