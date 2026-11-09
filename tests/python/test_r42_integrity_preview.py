"""FFmpeg and hashing contract tests for R4.2. NOT a C# application test."""
import hashlib
import io
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

try:
    from PIL import Image
except ImportError:
    Image = None


@unittest.skipUnless(shutil.which("ffmpeg") and Image, "FFmpeg and Pillow required")
class ExportComparisonContracts(unittest.TestCase):
    def test_source_bytes_changed_without_stat_change_have_different_sha(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "same-metadata.mp4"
            path.write_bytes(b"TESTDATA_12345678")
            before_stat = path.stat()
            before_sha = hashlib.sha256(path.read_bytes()).hexdigest()
            path.write_bytes(b"MODIFIED_12345678")
            os.utime(path, ns=(before_stat.st_atime_ns, before_stat.st_mtime_ns))
            self.assertEqual(path.stat().st_size, before_stat.st_size)
            self.assertEqual(path.stat().st_mtime_ns, before_stat.st_mtime_ns)
            self.assertNotEqual(hashlib.sha256(path.read_bytes()).hexdigest(), before_sha)

    def test_same_timestamp_preview_shows_changed_region(self):
        with tempfile.TemporaryDirectory(prefix="apg-r42-") as td:
            inp, out = Path(td) / "original.mp4", Path(td) / "masked.mp4"
            def run(*args):
                return subprocess.run(args, check=True, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, timeout=20).stdout
            run("ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "color=c=white:s=80x64:r=10:d=1",
                "-c:v", "mpeg4", str(inp))
            run("ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                "-i", str(inp), "-vf",
                "drawbox=x=10:y=10:w=30:h=30:color=black:t=fill:enable='between(t,0.3,0.7)'",
                "-c:v", "mpeg4", str(out))
            def px(path, time):
                image_bytes = run("ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error",
                                  "-ss", str(time), "-i", str(path), "-frames:v", "1",
                                  "-f", "image2pipe", "-c:v", "png", "pipe:1")
                return Image.open(io.BytesIO(image_bytes)).convert("RGB").getpixel((20, 20))[0]
            self.assertGreater(px(inp, 0.5), 220)
            self.assertLess(px(out, 0.5), 30)
            self.assertGreater(px(out, 0.1), 220)


if __name__ == "__main__":
    unittest.main()
