# Feature status — R4.3 (2026-11-09)

| Feature | Code state | Verification |
|---|---|---|
| R0–R1.1 image read/manual mask/zoom/SQLite | Authored | Windows C# compile and unit tests PASS; desktop UI NOT TESTED |
| R2 ONNX model adapter/diagnostics | Authored | Compiled; synthetic/mocked fixtures; real compatible exposure model NOT TESTED |
| R3 video seek/sample scan/motion interpolation | Authored | Compile/xUnit PASS; synthetic FFmpeg fixtures; desktop UI NOT TESTED |
| R4.0 batch scan/queue/resume | Authored | Compile/xUnit PASS; desktop UI NOT TESTED |
| R4.1 approved-only image/video batch export | Authored | Compile/xUnit PASS; FFmpeg synthetic tests PASS; desktop UI NOT TESTED |
| R4.2 image + sampled video before/after comparison | Authored | Compile/xUnit PASS; FFmpeg sample comparison PASS; desktop UI NOT TESTED |
| R4.2 SHA-256 bound video review / legacy reset | Authored | C# tests PASS; Python synthetic content-hash check PASS; desktop UI NOT TESTED |
| R4.2 sample audit timestamp | Authored | Compile/xUnit PASS; GUI/restart NOT TESTED |
| R4.3 Windows CI / executable artifact | Verified in CI | Main workflow PASS: build, 78 C# tests, 20 Python tests, publish, artifact upload |
| R5 live OBS/latency/fail closed | Not implemented | — |
| Dedicated validated exposure model | Not implemented | — |

`Implemented` or `compiled` does not imply reliable sensitive-content detection or operational safety. Full Windows WPF smoke testing and a suitable dedicated model are still required.

## R5.0 experimental core (2026-11-10)
- `LiveSafetyGate` and `LiveFramePipeline`: **code authored**, black-frame fallback tests added, Windows PR CI **PASS 89/89**; hardware/UI **NOT TESTED**.
- `DirectShowLiveCapture`: **code authored**, real Windows camera device run **NOT PERFORMED**.
- No OBS or virtual camera output, no persistent watchdog or validated sensitivity model; **not live-stream safe**.
- See `docs/R5_0_LIVE_CORE.md`.

## R5.1 OBS Browser Source prototype
- Core watchdog + BMP encoder: code integrated, C# tests PASS on GitHub Windows CI (98/98 core tests).
- Localhost sanitized-only Browser Source, generated URL token, HTML polling fallback: one Windows CI HTTP integration test PASS (1/1).
- Standalone executable with synthetic demo / experimental DirectShow: Windows publish artifact PASS, actual OBS desktop/camera smoke NOT TESTED.
- **Not production-ready.** No validated specialized exposure model, no completed real camera/OBS/manual end-to-end testing, no audio sync.
- Details: `docs/R5_1_OBS_BRIDGE.md`.

## R5.2 capture/OBS integration — PR CI passed
- Automatic camera reconnect with exponential backoff and blackout: **CI passed**, 103/103 core xUnit tests; real camera unplug NOT TESTED.
- Timeout on hung camera frame reads: **source authored**, physical device unplug test pending.
- OBS audio input manual sync offset via localhost WebSocket: **CI payload tests passed**; real OBS connection/sync calibration not tested.
- WPF experimental synthetic demo launcher with bundled bridge: **Windows WPF compile passed**, real desktop smoke pending.
- OBS renderer/process crash cannot be guaranteed to clear stale pixels; no dedicated verified exposure detector.
- See `docs/R5_2_RECOVERY_AUDIO.md`.
