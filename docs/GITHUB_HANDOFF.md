# GitHub / CI handoff — R4.3 bootstrap

## State as of preparation
- Baseline: R4.2 source, 99 source archive entries. No code behaviour changes in this bootstrap.
- Workflow prepared at `.github/workflows/windows-build.yml`.
- Local Python/FFmpeg suite: 20 tests passed on Linux during bootstrap.
- XML/XAML static checks: check with `python scripts/check_source.py`.
- GitHub repository: NOT YET CREATED / not uploaded by this preparation step.
- Windows Actions, C# build, WPF launch, ONNX model runtime: NOT VERIFIED.

## Initial Actions stages
checkout > .NET 10 > ffmpeg tools > static > Python integration > restore > build > xUnit > publish win-x64 > check EXE > artifact upload.

## Next work
1. User creates private `mibesamiti-png/ai-privacy-guard`; initial push via `scripts/first-push.ps1`.
2. Review first workflow result and fix compiler/XAML errors, if any. Do not hide errors by excluding tests.
3. After green CI, do Windows desktop smoke tests (image load, crop, review, video masks, batch output and restart).
4. Only then continue R4.3 stability work, R5 live mode after explicitly verified fail-closed chain.

## Data protection
No user-submitted media should be tracked. Synthetic fixtures only. Model binaries deliberately ignored by git.
