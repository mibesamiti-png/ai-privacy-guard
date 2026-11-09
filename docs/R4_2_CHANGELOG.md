# R4.2 — Batch comparison + source-bound video approvals
Date: 2026-11-09. Status: source authored, Windows compile/runtime unverified.

## Added
- Before/after side-by-side comparison for completed batch image outputs; in-memory downsampled previews, no thumbnails stored.
- Video sample comparison at user-specified seconds using FFmpeg frame extraction for both original and guarded MP4. The frame is *approximately* seeked, not exact frame-aligned for VFR; a sample cannot certify a complete video.
- Preview hash gate: original, current review fingerprint and output SHA-256 must match completed receipt before enabling visual inspection; revalidation before writing the sample-review timestamp.
- Visual sample audit fields on export receipt (`PreviewCheckedAtUtc`, `PreviewCheckedAtUs`). Distinct from `Completed` export status and NOT a claim of safe content.
- Video review metadata schema 2 includes source SHA-256. Reusing stale approval requires exact bytes; metadata alone is insufficient.
- Legacy schema 1 review projects load with *all* decisions reset to Pending. User must revisit and approve; saving produces schema 2 and does not silently preserve old approvals.
- Hash of video files for ordinary review load performed off UI thread; application review saves reuse the hash recorded when loaded (batch export independently re-hashes).
- Windows CI artifact name updated to R4.2, R4.2 C# core unit tests added, static check now validates comparison UI control names.

## Constraints
- Model is not bundled and no real exposure detections were evaluated.
- Local C#/.NET SDK was unavailable, so **no C# compile, xUnit execution, WPF GUI or native ONNX inference was verified here**.
- FFmpeg/Python tests establish video-processing and content-hashing assumptions, not the correctness of C# event handlers.
- SHA-256 reading may be slow for very large files; use an asynchronous save pipeline in a future performance round.
- Windows file junction/reparse-point hardening and file-change race defenses are not complete.
- The compare viewer stores only the *most recently inspected sample timestamp*, not full-video confirmation.
