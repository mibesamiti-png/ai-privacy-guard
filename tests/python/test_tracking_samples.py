"""R3.3 real FFmpeg segment-sampler smoke tests on artificial rectangles.
This DOES NOT run the C# block matcher or prove sensitive-content detection.
"""
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

FFMPEG = shutil.which('ffmpeg')
WIDTH, HEIGHT = 160, 112
FPS = 8


def synthetic_frame(x, y):
    frame = bytearray(bytes((20, 20, 20, 255)) * (WIDTH*HEIGHT))
    for yy in range(28):
        for xx in range(28):
            v = 60 + (xx*23 + yy*17 + xx*yy*3) % 195
            idx = ((y+yy)*WIDTH + x+xx)*4
            frame[idx:idx+4] = bytes((v, v, v, 255))
    return frame


@unittest.skipUnless(FFMPEG, 'ffmpeg is not installed')
class SegmentSamplerTests(unittest.TestCase):
    def run_ffmpeg(self, args, input_data=None):
        process = subprocess.run([FFMPEG, '-nostdin', '-hide_banner', '-loglevel', 'error',
                                  '-y', *args], input=input_data, capture_output=True, timeout=25)
        if process.returncode:
            raise AssertionError(process.stderr.decode(errors='replace'))
        return process.stdout

    def test_lossless_segment_sampling_position_and_duration(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = str(Path(tmp) / 'motion.mkv')
            raw = b''.join(synthetic_frame(40 + i*3, 35 + i) for i in range(FPS * 2))
            self.run_ffmpeg(['-f', 'rawvideo', '-pix_fmt', 'bgra', '-s', f'{WIDTH}x{HEIGHT}',
                             '-r', str(FPS), '-i', 'pipe:0', '-an', '-c:v', 'ffv1', source], raw)
            segment = self.run_ffmpeg(['-ss', '0.5', '-i', source, '-t', '1', '-map', '0:v:0',
                                       '-vf', f'fps={FPS}:start_time=0,scale={WIDTH}:{HEIGHT}:flags=fast_bilinear,format=bgra',
                                       '-an', '-sn', '-pix_fmt', 'bgra', '-f', 'rawvideo', 'pipe:1'])
            nbytes = WIDTH*HEIGHT*4
            self.assertEqual(8, len(segment)//nbytes)
            for index in range(8):
                i = index+4
                x, y = 40+i*3, 35+i
                bytepos = index*nbytes + ((y+14)*WIDTH + x+14)*4
                self.assertGreater(segment[bytepos], 60)
                before = index*nbytes + ((y+14)*WIDTH + x-3)*4
                self.assertEqual(segment[before], 20)

if __name__ == '__main__':
    unittest.main()
