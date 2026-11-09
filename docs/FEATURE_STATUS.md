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
- `LiveSafetyGate` and `LiveFramePipeline`: **code authored**, black-frame fallback tests added, Windows CI **pending**.
- `DirectShowLiveCapture`: **code authored**, real Windows camera device run **not performed**.
- No OBS or virtual camera output, no persistent watchdog or validated sensitivity model; **not live-stream safe**.
- See `docs/R5_0_LIVE_CORE.md`.
