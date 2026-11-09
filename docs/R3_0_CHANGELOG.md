# R3.0 — video preview and manual review milestone

Date: 2026-11-09. All previously authored R2.2 code retained.

## Added

- `src/PrivacyGuard.Media`: `FfmpegVideoService` with safe subprocess arguments (no shell), FFprobe metadata, bounded-memory PNG extraction, cancellation/timeout, static-mask FFmpeg MP4 export and temporary-file cleanup.
- `src/PrivacyGuard.Core/VideoMaskPlan.cs`: immutable `VideoMaskCandidate`, range/normalized-coordinate validation, approved-only drawbox expression creation.
- `src/PrivacyGuard.App/VideoReview.xaml.cs` and a video WPF tab with time slider, ±1s and ±estimated-frame seek, crop-zoom, manual box selection, start/end markers, review decisions, optional AI-on-one-frame, export/cancel buttons.
- `src/PrivacyGuard.App/Services/VideoReviewStore.cs`: local JSON persistence keyed by video path/size/modification time, with atomic replace. No frame bytes stored.
- `ImageCodec.ReadEncodedBytes`: decode FFmpeg PNG bytes to existing `PixelBuffer` without generating temporary images.
- `VideoMaskPlanTests.cs`: C# pure-core regression tests, unexecuted here.
- `tests/python/test_video_pipeline.py`: actual FFmpeg pipeline test using synthetic video with audio, passing here.

## Key limitations

1. The R3.0 video player samples at requested timestamps; **actual neighboring-frame PTS indexing is not implemented**.
2. No full-video model scan, tracking, optical flow, automatic temporal mask propagation, or model accuracy assessment.
3. Exports only **approved fixed black rectangles** at specified intervals; an object moving outside a rectangle may remain visible.
4. No video scene-change detection, batch processing or OBS connection.
5. No .NET SDK here, so Windows compilation, WPF lifecycle, FFmpeg invocation from C# and ONNX inference are **unverified** despite FFmpeg command strategy integration passing.

## Next technical milestone

**R3.1**: Windows CI compile-and-fix; obtain exact PTS frame list for VFR/accurate single-step; video-wide sampled detection with progress/cancel; connect grouped AI candidates to review queue; per-frame tracking/projection and a safe mask-preview mode. Do not claim protected video output until tracking, temporal coverage and edge-case checks are tested.
