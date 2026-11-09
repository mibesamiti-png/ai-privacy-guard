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
            for fps in (1, 2, 4, 8, 12):
                raw = call('ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error',
                           '-i', str(src), '-map', '0:v:0', '-vf',
                           f'fps={fps}:start_time=0,scale=160:96:flags=fast_bilinear,format=bgra',
                           '-an', '-sn', '-pix_fmt', 'bgra', '-f', 'rawvideo', 'pipe:1')
                framesize = 160 * 96 * 4
                self.assertEqual(len(raw) % framesize, 0)
                self.assertEqual(len(raw) // framesize, 3 * fps)
                self.assertNotEqual(raw[:framesize], raw[framesize:2 * framesize])
            self.assertEqual(before, hashlib.sha256(src.read_bytes()).hexdigest())

    def test_dense_12fps_can_observe_a_brief_pulse_missed_by_1fps(self):
        # A consent-safe artificial movie. White for source frames 7..10
        # (133ms), black otherwise. This proves improved *opportunity* to
        # examine brief events; it is not a claim of 100% temporal coverage.
        with tempfile.TemporaryDirectory() as folder:
            src = Path(folder) / 'brief_pulse.mkv'
            width, height, fps = 64, 64, 30
            raw = bytearray()
            for frame in range(fps):
                level = 255 if 7 <= frame <= 10 else 0
                raw.extend(bytes([level]) * (width * height * 3))
            subprocess.run([
                'ffmpeg', '-nostdin', '-loglevel', 'error', '-f', 'rawvideo',
                '-pix_fmt', 'rgb24', '-s', f'{width}x{height}', '-r', str(fps),
                '-i', 'pipe:0', '-c:v', 'ffv1', '-y', str(src)
            ], input=bytes(raw), check=True, capture_output=True)

            def sample(rate):
                data = call('ffmpeg', '-nostdin', '-loglevel', 'error',
                            '-i', str(src), '-vf', f'fps={rate}:start_time=0,format=gray',
                            '-an', '-sn', '-f', 'rawvideo', '-pix_fmt', 'gray', 'pipe:1')
                frame_size = width * height
                self.assertEqual(len(data) % frame_size, 0)
                return [max(data[i:i + frame_size])
                        for i in range(0, len(data), frame_size)]

            slow = sample(1)
            fast = sample(12)
            self.assertEqual(len(slow), 1)
            self.assertEqual(len(fast), 12)
            self.assertLess(max(slow), 20, '1fps should miss the short artificial pulse')
            self.assertGreater(max(fast), 235, '12fps should sample the pulse')
            self.assertLess(min(fast), 20, 'Only a few high-density samples contain the pulse')

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
