# R3.1 release notes — 2026-11-09

## Source changes
- Add `PrivacyGuard.Core/VideoFrameIndex.cs`: sorted, distinct source-frame PTS index with nearest/adjacent methods.
- Add `FfmpegVideoService.IndexFrameTimestampsAsync`: bounded ffprobe frame-time collection. No full-frame pixel decode for indexing.
- Add `FfmpegVideoService.StreamSamplesAsync`: streaming raw BGRA frame samples using `fps` filter at 1/2/4fps, max 30,000 frames, downscaled to maximum dimension 640.
- Extend `VideoReview.xaml.cs` with scan state, cancellation, PTS indexing, grouping, review-first queue insertion, duplicate suppression and progress.
- Extend video tab UI and status text. Preserve manual mask and FFmpeg export.
- Add synthetic FFmpeg/ffprobe Python tests and C# `VideoFrameIndexTests` source.

## Important limitations
- **No bundled ONNX model or verified exposure detection.** External 6-column compatible ONNX weights and manifest required.
- Sampled inspection can miss exposure lasting less than the sample interval. Events are approximate and use **one stationary mask rectangle** even if objects move.
- PTS indexing improves time stepping, but `ExtractPngAsync` still uses time-based FFmpeg seeking; adjacent source frame pixel identity is not guaranteed.
- Full AI scan and GUI are **not compiled or run** in current Linux environment; only FFmpeg strategy integration, source static checks and Python tests are verified.
- Source is not uploaded to GitHub/Notion because there is no confirmed Privacy Guard repository or explicit publication request.

## Next
1. Run Windows `scripts/build.ps1` and `dotnet test`; fix compiler/runtime issues.
2. Use trusted synthetic or permissioned clips to validate scan results against timestamp and video source size; verify cancel and rescans.
3. R3.2: time-varying masks via track keyframes, interpolation and guarded export. Avoid labeling this as verified exposure protection before comprehensive evaluation.
