# R4.1 — Approved-only batch export contract

## Inputs and user intent

- The batch inventory comes from R4.0's folder scan; manual region reviews are valid even when an ONNX model is not loaded.
- For an image, locate the original reviewed source by its full path AND SHA-256. Never silently create a new reviewed record.
- For a video, load the source-bound VideoReviewStore metadata. Existing video review files bind by path, byte length and modification time. **Content-hash binding is not yet part of the video review store; this is a residual risk if bytes change without changing the metadata.**
- Eligible files must have >=1 `Approved` mask and **zero** `Pending` or `Deferred` candidates. `Rejected` candidates are not masked. No candidate means `NeedsReview`, never an unmodified output.
- A completed AI scan does not imply the content is safe. Sampling gaps remain.

## Output naming

- Default destination: `<input root>/AIPrivacyGuard_Output` (excluded from the R4 scanner).
- Custom output destinations must be outside the input tree, or under the default excluded output folder.
- Source hierarchy is preserved. `nested/scene.jpg` -> `AIPrivacyGuard_Output/nested/scene__jpg_guarded.png`; `clip.mkv` -> `clip__mkv_guarded.mp4`.
- Input extension marker prevents `.jpg` / `.png` basename collisions. Existing outputs cannot be overwritten; a unique `__2`, `__3`, etc. suffix is chosen when needed.
- The original media is never opened for writing. No media is copied to an external server.

## Atomic job completion

1. Validate source path and stat; hash source contents with SHA-256.
2. Load review decisions, check approval gate; fingerprint all review records AND image processing settings.
3. If the saved successful receipt matches source hash, review fingerprint, intended output path AND output hash, reuse it without re-encoding.
4. Otherwise choose an unused filename, persist `Running`, encode a hidden `.staged.png/.staged.mp4` file.
5. Recheck source size/mtime/hash and current decisions; compute the temporary output's hash.
6. Atomically move staging to final with `overwrite:false`, persist `Completed` receipt. A source or review change before publication produces an error rather than a false success.
7. On cancel/failure, clean staging files in `finally`. If the *process* is killed, an orphaned staging file may require manual cleanup; it must never be treated as a finished export.

## Limitations

- The video exporter currently creates a black box driven by interpolated keyframes; this is not optical-flow-corrected censoring or verified coverage.
- The video detection scan samples sparsely (1fps in R4.0 batch). Missed exposure events cannot be inferred from zero findings.
- Real-world model quality, image/video export in the WPF app, and Windows build still need verification. FFmpeg command-level tests are not equivalent to WPF UI testing.
- Review metadata may be modified by another process; current pipeline checks again before publishing but concurrent filesystem tampering or hardlinks are not fully defended.
- Outputs and receipts include local user file paths; protect `%LOCALAPPDATA%/AIPrivacyGuard` and the output folder. Never share original media or review crops without consent.
