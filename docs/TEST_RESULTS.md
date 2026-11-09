# Verification report — R4.3 (2026-11-09)

## GitHub Actions verification
- **Main PASS**: [Windows workflow #42666438651](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651), squash-merged commit `d77d7c2080167c7bd662713064cb8fb38de3cca5`.
- **PR PASS**: [Windows workflow #42666001582](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666001582), fix head `09baf07226928214e597929930a58618dee635b6`.
- .NET 10 SDK and FFmpeg setup, static source checks, Python/FFmpeg tests (20), NuGet restore, `dotnet build`, `dotnet test`, Windows x64 publish, EXE existence and artifact upload: all PASS.
- xUnit result on PR: **78 passed, 0 failed, 0 skipped**. Main xUnit step also PASS.
- [Main build artifact](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651/artifacts/13696203027) exists (zip archive size approx 73 MB). This only confirms the published files are present.

## Not yet tested
- Interactive WPF application launch and interaction on a Windows desktop.
- Real-image/video end-to-end manual acceptance and exported artifact comparison through the WPF GUI.
- Specialized exposure-detection model integration and classification performance.
- Live OBS fail-closed output safety.

## Historical R4.2 local-only results
- `python scripts/check_source.py`: PASS (XAML/XML, project refs, event names).
- `python -m unittest discover -s tests/python -v`: PASS 20/20 using synthetic media.
- Same-file metadata with changed bytes correctly detected by SHA-256 synthetic fixture.
- FFmpeg same-time comparison and moving mask synthetic fixtures PASS.

### Interpret results carefully
Green CI is a real compiler/test/publish verification. It does **not** prove correctness of untested WPF interaction, actual sensitive-content detection, or live stream safety.

## R5.0 live safety core — verified PR CI (2026-11-10)
- PR #3 merged as `a67133657f0a662dd04cd04bd543c71a31d26231`.
- [GitHub Actions #42670038848](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670038848): Windows build/publish PASS, C# xUnit **89 passed/0 failed**, Python/FFmpeg PASS, artifact upload PASS.
- 11 new synthetic live-gate tests, including blackout on missing detector, timeout, exception, malformed detections, stale/concurrent frames and disable-during-inference.
- **NOT RUN**: real DirectShow camera device smoke, interactive UI, OBS output, audio sync, exposure detector accuracy and downstream heartbeat watchdog.
- main push [#42670458131](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670458131): conclusion must be checked separately.

## R5.1 bridge checks (pending CI)
- Core watchdog stale-timeout and BMP encoder tests added. NOT YET EXECUTED.
- Localhost Browser Source HTTP synthetic integration test added. NOT YET EXECUTED.
- Windows standalone console publish step added. NOT YET EXECUTED.
- Real device, WPF UI coupling and actual OBS desktop integration: NOT TESTED.
- This build is **not appropriate for live-stream privacy protection**.
