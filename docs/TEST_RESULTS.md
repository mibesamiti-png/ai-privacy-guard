# Verification report — R4.2 (2026-11-09)

| Command or scenario | Result | Scope |
|---|---|---|
| `python scripts/check_source.py` | PASS | XML/XAML structure, project refs, 74 event names, comparison control names |
| `python -m unittest discover -s tests/python -v` | PASS, 20/20 | Python/FFmpeg synthetic media and hashing semantics |
| Same-length/mtime file content mutation | PASS | SHA-256 detects changed bytes in isolated Python fixture |
| FFmpeg original/masked same-time PNG extraction | PASS | Synthetic video comparison harness |
| Existing video output tests | PASS | MP4 black-box interval and audio retention, independent Python harness |
| C# `VideoReviewBindingTests` | WRITTEN, NOT RUN | Dotnet SDK unavailable locally |
| C# all project xUnit tests | NOT RUN | Dotnet SDK unavailable locally |
| WPF build, image/video UI, batch export | NOT RUN | Windows environment unavailable locally |
| Actual ONNX exposure inference | NOT RUN | Compatible model and Windows runtime not supplied |

Passing the Python suite does NOT prove the WPF app compiles or runs. Windows CI on push/PR should be checked on a repository with this source before claiming an executable build.
