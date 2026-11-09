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

## R5.1 bridge checks — verified PR CI (2026-11-10)
- Core watchdog stale-timeout and BMP encoder tests: PASS within **98/98** core xUnit suite.
- Localhost Browser Source HTTP synthetic integration: PASS **1/1**.
- Windows standalone console publish and artifact upload: PASS.
- - Passing PR CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42673993149 on commit `ff88600a6c41f35a2317a506237e57472577ea80`.
- Main squash commit: `0c758a8b53cb9a36af93a5c6219a6a09d0f7e236`.
- Main CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42674405361 (verify final conclusion).
- Real DirectShow camera device, WPF UI coupling, and actual OBS desktop integration: **NOT TESTED**.
- This build is **not appropriate for live-stream privacy protection**.

## R5.2 verified PR CI (2026-11-10)
- PR #7 success: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42677140187
- Main merge commit: `114003647c5b24c38803602234fab48695cb390d`.
- Main CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42677559049 (check conclusion separately).
- WPF/LiveBridge publish PASS; WPF normal artifact includes the live-bridge subfolder.
- New manual OBS audio offset protocol and synthetic capture reconnect tests have passed; real capture and OBS still require local tests.

- New capture reconnect/backoff/monotonic/fault/cancellation synthetic xUnit tests: **PASSED** in 103/103 core xUnit tests on Windows CI.
- OBS audio offset validation/payload tests: **PASSED** in 7/7 OBS transport/audio protocol xUnit tests.
- WPF experimental launcher and bridge bundled publish: Windows compile and artifact publish **PASSED**; actual WPF GUI run NOT TESTED.
- Physical DirectShow camera reconnect, OBS renderer restart, manually measured A/V sync: NOT TESTED.
- Not ready to prevent privacy incidents during live broadcasts.

## R6 model integration — verified PR CI (2026-11-10)
- PR run: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42687318433 — PASS.
- C# core xUnit 109/109; OBS 7/7; actual CPU model smoke PASS with synthetic solid 0 and 90 images, both returning 0 candidates as expected for trivial images.
- Main merge `69d095ad87fd2e3110237633e7ee40eee21ac5b4`; main CI https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42687745033 (check final conclusion separately).

- YOLOv8 raw-head and square-pad geometry tests PASS within core C# xUnit suite 109/109.
- Real pinned 320n ONNX download SHA verified and ONNX CPU inference executed twice on synthetic frames; MODEL_SMOKE_PASS in PR CI #42687318433.
- Real-world detection performance, particularly brief nipple exposure or underwear seen under skirts, NOT TESTED.
- No personal media or model weights are bundled with the public repository.

## R6 external real-media cross-domain probe (2026-11-09)
- [Windows CI #42694286982](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42694286982): actual NudeNet 320n ONNX inference PASS, all 7 downloads PASS.
- 1 of 7 adult-character/real-fashion images yielded an AI candidate; fashion photo had `FEMALE_GENITALIA_COVERED`, score `0.665824`, at `(289,525,65,70)` in a 482x720px image.
- 2D Yor and Ningguang and 3D Raiden and Ningguang, plus real clothed cosplay controls: all zero. These are NOT positive panty-shot images.
- All third-party source image data removed before uploading results. Results include source links, SHA-256, JSON and abstract box-only diagrams.
- Not a sensitivity/recall benchmark. See `docs/R6_REALMEDIA_EVAL_RESULTS.md`.
