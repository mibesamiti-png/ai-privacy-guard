# R3.2 change log — 2026-11-09

## Scope
1. Add `VideoMaskKeyframe` to video review candidates while retaining the previous optional metadata layout.
2. Add `VideoMotionTrack.BoundsAt` for linear position/size interpolation, plus sorted upsert, bounds and time checks.
3. Wire **add/update** and **delete nearby** keyframe controls to R3.1 video review. Selected moving candidates use interpolated rectangles in overlay and crop.
4. Keep sampled model detections as tentative keyframes and mark all newly auto-detected events as Pending.
5. Add `VideoMotionExportPlan` to build FFmpeg `sendcmd` + `drawbox` filter graph; write and delete temporary command file around export.
6. Add xUnit tests for keyframe validation, interpolation, filter construction, and status handling (source only; not executed).
7. Add real FFmpeg tests for movement, disabled time ranges, untouched source and audio retention; 16 Python tests pass.

## Risks
- Not optical flow or high-confidence tracking. Rapid motion, scene cuts, VFR offsets and missed detections remain potentially unsafe.
- C# / WPF / native ONNX not compiled or executed here. Status is **source authored**, not verified Windows release.
- Subject-specific exposure model not provided; no reliable automatic exposure detection.

## Next
- Windows `dotnet restore/build/test` and hands-on GUI tests; repair issues if encountered.
- R3.3 consider optical-flow-assisted mask suggestion with review and failure analysis, plus FFmpeg output inspection for edge cases.
- R4 batch folder processing only after a stable Windows build and clear errors/rollback policy.
