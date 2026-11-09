"""Check actual FFmpeg sendcmd/drawbox behavior on synthetic, non-person video.

The test validates the FFmpeg technique, not the C# UI or ONNX detection.
"""
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

FFMPEG = shutil.which('ffmpeg')
FFPROBE = shutil.which('ffprobe')


@unittest.skipUnless(FFMPEG and FFPROBE, 'FFmpeg/FFprobe not installed')
class MovingMaskExportTests(unittest.TestCase):
    def run_ffmpeg(self, args, cwd=None):
        p = subprocess.run([FFMPEG, '-nostdin', '-hide_banner', '-loglevel', 'error', '-y', *args],
                           capture_output=True, cwd=cwd, timeout=30)
        if p.returncode:
            raise AssertionError(p.stderr.decode(errors='replace'))
        return p.stdout

    def test_moving_mask_changes_position_and_stops_outside_interval(self):
        with tempfile.TemporaryDirectory() as d:
            folder = Path(d)
            original = folder / 'orig.mp4'
            masked = folder / 'guarded.mp4'
            self.run_ffmpeg(['-f', 'lavfi', '-i', 'color=c=white:s=160x80:r=10:d=1.5',
                             '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=16000:duration=1.5',
                             '-shortest', '-c:v', 'libx264', '-crf', '0', '-pix_fmt', 'yuv420p',
                             '-c:a', 'aac', str(original)])
            before = original.read_bytes()
            commands = folder / 'apg_motion_test.txt'
            # User keyframes (t=.2s, x=20) and (t=1.0s, x=100), 10fps.
            lines = []
            for i in range(1, 8):
                t = .2 + i * .1
                x = 20 + 10*i
                lines.append(f'{t:.6f} drawbox@guard0 x {x};')
            commands.write_text('\n'.join(lines) + '\n')
            mask_filter = ("sendcmd=f=apg_motion_test.txt,"
                           "drawbox@guard0=x=20:y=24:w=16:h=16:color=black:t=fill:"
                           "enable='gte(t,0.2)*lt(t,1.0)'")
            self.run_ffmpeg(['-i', str(original), '-map', '0:v:0', '-map', '0:a?',
                             '-vf', mask_filter, '-c:v', 'libx264', '-crf', '0',
                             '-pix_fmt', 'yuv420p', '-c:a', 'aac', str(masked)], cwd=d)
            self.assertEqual(before, original.read_bytes(), 'Source must not change')
            probe = subprocess.run([FFPROBE, '-v', 'error', '-select_streams', 'a:0',
                                    '-show_entries', 'stream=codec_name', '-of', 'default=nw=1', str(masked)],
                                   capture_output=True, timeout=15)
            self.assertIn('aac', probe.stdout.decode())
            # Exact raw decoded RGB samples. Input contains plain white (no personally identifying media).
            output = self.run_ffmpeg(['-i', str(masked), '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'])
            frame_size = 160*80*3
            self.assertEqual(len(output)//frame_size, 15)
            def at(frame, x, y):
                offset = frame*frame_size + (y*160+x)*3
                return output[offset:offset+3]
            self.assertTrue(all(ch >= 240 for ch in at(0, 25, 30)))
            self.assertTrue(all(ch <= 25 for ch in at(3, 31, 30)))
            self.assertTrue(all(ch >= 240 for ch in at(3, 100, 30)))
            self.assertTrue(all(ch <= 25 for ch in at(8, 81, 30)))
            self.assertTrue(all(ch >= 240 for ch in at(11, 80, 30)))

    def test_reject_unavailable_ffmpeg_filter_commands(self):
        # A sendcmd "reinit" command can be unsupported; check per-option command path instead.
        with tempfile.TemporaryDirectory() as d:
            commands = Path(d) / 'cmd.txt'
            commands.write_text('0.5 drawbox@guard0 x 40;\n')
            raw = self.run_ffmpeg(['-f', 'lavfi', '-i', 'color=c=white:s=80x40:r=4:d=1',
                                   '-vf', "sendcmd=f=cmd.txt,drawbox@guard0=x=5:y=10:w=10:h=10:color=black:t=fill",
                                   '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'], cwd=d)
            size = 80*40*3
            def pixel(frame, x, y):
                i = frame*size+(y*80+x)*3
                return raw[i:i+3]
            self.assertTrue(all(v <= 25 for v in pixel(0, 10, 15)))
            self.assertTrue(all(v <= 25 for v in pixel(3, 44, 15)))
            self.assertTrue(all(v >= 240 for v in pixel(3, 10, 15)))

if __name__ == '__main__':
    unittest.main()
