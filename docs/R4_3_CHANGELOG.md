# R4.3 — First passing Windows CI (2026-11-09)

## Verified result
- [Passing pull-request run #42666001582](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666001582) on commit `09baf07226928214e597929930a58618dee635b6`.
- Windows x64 / .NET 10: restore, build, publish, executable existence check, and artifact upload passed.
- C# xUnit: **78 passed, 0 failed**.
- Python/FFmpeg synthetic integration: **20 passed**.
- [Build artifact](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666001582/artifacts/13696846313): `AIPrivacyGuard-R4.2-Windows-x64` (entire publish folder; approx. 73 MB archive).
- PR #1 merged via squash as `d77d7c2080167c7bd662713064cb8fb38de3cca5`.

## Code fixes
- Resolved WPF `Path` ambiguity against `System.IO.Path` in main image and video review partial classes.
- Replaced three unsuitable target-typed `new()` uses in keyframe tests with explicit constructors.
- Fixed unintended object/reference comparison in the video selection overlay.
- Made normalized-rectangle floating-point test compare coordinates with tolerance.

## Still NOT verified
- No interactive Windows UI smoke test has been completed.
- Windows CI confirms an EXE file was published, **not** that the WPF window launched or the end-to-end workflows run correctly.
- No specialized exposure-detection model was included or evaluated.
- Sparse video sampling can miss frames; outputs must not be presented as guaranteed safe.
- R5 real-time OBS and fail-closed streaming are not implemented.

See `docs/SMOKE_TEST_CHECKLIST.md` for the next test sequence.
