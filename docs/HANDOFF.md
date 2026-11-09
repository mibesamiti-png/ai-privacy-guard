# AI Privacy Guard R4.3 — GitHub handoff (2026-11-09)

## Current source of truth
- Repository: https://github.com/mibesamiti-png/ai-privacy-guard (Public; never commit private/user media or personal review databases).
- Main HEAD after R4.3 build fixes: `d77d7c2080167c7bd662713064cb8fb38de3cca5`.
- PR #1 merged: https://github.com/mibesamiti-png/ai-privacy-guard/pull/1
- **Main Windows CI PASS**: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651
- Build artifact: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651/artifacts/13696203027

## Verified
- Windows x64/.NET 10 full solution build succeeded.
- All 78 xUnit tests passed on the PR head; main's build/test/publish stages also passed.
- Python/FFmpeg synthetic integration suite: 20 tests passed.
- Windows publish directory uploaded as an artifact containing the executable.

## NOT verified
- EXE starts, WPF window and event handlers work on a user's desktop; no interactive smoke test has been performed.
- End-to-end manual masking, export, batch resume and before/after compare in the actual Windows UI.
- Compatible specialized exposure-detection model, inference quality and recall (model not bundled).
- Safe real-time OBS capture/output (R5 not implemented); DO NOT use for live exposure-prevention yet.

## Highest priority next steps
1. Download the main build artifact, extract the full directory and perform `docs/SMOKE_TEST_CHECKLIST.md` with **synthetic** test files.
2. Fix WPF launch/runtime issues found by those tests and rerun CI; record outcomes and logs in `docs/TEST_RESULTS.md`.
3. Run an offline end-to-end image/video/batch test, including Pending export blocked and SHA-256 review invalidation on modified source.
4. Measure high-resolution hashing, FFmpeg process cancellation, long-video memory, VFR timestamps and mask tracking failure handling.
5. Evaluate a legitimately licensed compatible model on an approved labeled dataset before claiming sensitive-exposure detection capabilities.
6. Only after Windows application workflows and accuracy are reviewed: start R5 with a fail-closed output design.

## Build process
- Branch changes via PR; CI on PR and main push.
- Required green steps: source checks → Python/FFmpeg → restore → compile → xUnit → publish → artifact.
- A published EXE file is **not** equivalent to a tested WPF application.
- See `docs/R4_3_CHANGELOG.md`, `docs/FEATURE_STATUS.md` and `docs/SMOKE_TEST_CHECKLIST.md`.

## New R5.0 development branch (2026-11-10)
- Branch: `feature/r5-live-safety-core` **merged** via PR #3; main commit `a67133657f0a662dd04cd04bd543c71a31d26231`.
- Added experimental black-frame `LiveSafetyGate`, sanitized-only `LiveFramePipeline`, and Windows DirectShow capture adapter.
- Synthetic xUnit tests: **89/89 passed** in PR CI [#42670038848](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670038848). Full Windows camera hardware and UI integration **NOT TESTED**.
- R5.0 remains **headless and not ready for broadcast**: no output/watchdog/OBS and no validated exposure detection model.
- Next action: verify main CI [#42670458131](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670458131), then implement downstream watchdog and sanitized output. See `docs/R5_0_LIVE_CORE.md`.

## R5.1 implemented and merged, CI verified
- Merged PR #5 into main, commit `0c758a8b53cb9a36af93a5c6219a6a09d0f7e236`.
- Adds `LiveOutputWatchdog`, `BmpFrameEncoder`, `LocalObsBrowserSource`, and `AIPrivacyGuard.LiveBridge` standalone console.
- OBS integration is a localhost Browser Source that receives only watchdog snapshots; separate from main WPF UI. Synthetic `--demo` needs no camera/model.
- New core watchdog tests and localhost HTTP integration test: Windows CI passed, **98/98 core tests, 1/1 browser test**. Both executable artifacts uploaded.
- Read `docs/R5_1_OBS_BRIDGE.md` before changing architecture. No personal media in public GitHub.
- Next action: verify the integrated `main` CI, run synthetic Windows/OBS manual smoke, then test real camera disconnect using non-sensitive footage; do not use for production live privacy.
