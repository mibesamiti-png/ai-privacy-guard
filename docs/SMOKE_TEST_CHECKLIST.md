# R4.3 Windows manual smoke test checklist

## Before testing
1. Open the latest green Windows GitHub Actions run, download the `AIPrivacyGuard-R4.2-Windows-x64` artifact ZIP, and extract **all files** to a dedicated directory; do not run an EXE directly from inside the ZIP.
2. Run `AIPrivacyGuard.exe` on Windows and record OS version, whether it opens, exact error text, and whether logs appear.
3. For privacy, use the repository's **synthetic** sample media, not private photos or videos. Do not commit user videos, local paths, generated crops, or application database files to public GitHub.
4. Keep the original sample media unchanged; confirm exports land in a separate output directory.

## Main flows
- [ ] Image: open `samples/synthetic_demo.png`, manually add a mask, crop, zoom 1x/2x/4x/8x, move and resize, undo.
- [ ] Image: approve one region, reject another, export, verify only approved region is changed.
- [ ] Image: restart application, verify decisions persist; do not overwrite the source.
- [ ] Video: open `samples/synthetic_video.mp4`, scrub, step frames, set in/out times, manually add mask.
- [ ] Video: add two motion keyframes, preview tracking, export masked MP4, verify original frames outside interval and audio still present.
- [ ] Batch: open `samples/batch_fixture`, scan files, cancel and resume; check no duplicate jobs.
- [ ] Batch export: leave one candidate Pending and confirm export is blocked; approve it and retry.
- [ ] Comparison: inspect original and exported images; for video inspect 0.1 s and 0.5 s samples, then save an audit marker. A sample audit is **not** whole-video certification.
- [ ] Integrity: edit a synthetic source file after review, verify old approvals are invalidated and re-review is requested.
- [ ] Error handling: intentionally select an unsupported input; verify clear error and application remains responsive.

## Record results
For each test, record `PASS / FAIL / NOT RUN`, precise steps, expected vs actual behavior, and app log excerpt. Screenshots may be added only when they contain **synthetic** content and no personal identifiers. File issues with reproducible failures; do not claim WPF runtime is verified before the checklist has actually run.
