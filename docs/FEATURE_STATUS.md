# Feature status — R4.2

| Feature | Code state | Verification |
|---|---|---|
| R0–R1.1 image read/manual mask/zoom/SQLite | Authored | Structural only; Windows UI not tested |
| R2 ONNX model adapter/diagnostics | Authored | Mock fixtures; native ONNX not tested; specialist model missing |
| R3 video seek/sample scan/motion interpolation | Authored | Synthetic FFmpeg/Python fixtures; Windows UI not tested |
| R4.0 batch scan/queue/resume | Authored | C# xUnit drafted, not run |
| R4.1 approved-only image/video batch export | Authored | Synthetic FFmpeg output test, WPF untested |
| R4.2 image + sampled video before/after comparison | Authored | FFmpeg timestamp comparison test passed; WPF untested |
| R4.2 source SHA-256 bound video review / legacy reset | Authored | C# test drafted; Python content hash contract test passed; WPF untested |
| R4.2 sample audit timestamp | Authored | C# test drafted; actual GUI/restart untested |
| R5 live OBS/latency/fail closed | Not implemented | — |
| Dedicated validated exposure model | Not implemented | — |

Do not interpret source state as deployed or validated capability.
