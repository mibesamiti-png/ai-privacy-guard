# Batch queue contract (R4.0)

## User controls

- Folder Select -> Scan (non-destructive file inventory).
- Recursive checkbox applies during Scan. Changing it requires a new Scan before Run.
- AI Run / Resume requires an explicitly loaded compatible ONNX model from the image tab.
- Cancel requests cancellation of the current job. Processing can take time to exit native ONNX/FFmpeg calls.
- Retry Errors changes `Failed` to `Pending`; Reset All changes every job to `Pending`. Neither deletes previous human review decisions.
- Open Selected loads the file's locally saved Pending review candidates into the original image/video tab.

## State machine

`Pending -> Running -> Completed | Failed`

- A cancellation maps the current job back to `Pending` and stops advancing the queue.
- An app crash leaves `Running` in the snapshot; Scan converts it back to `Pending`.
- Completed/Failed are preserved on Scan only when source path, media kind, file size and last-write time all match.
- A changed file resets to Pending and must be analyzed again.
- No candidate is added to a persistent review source until the entire **file** analysis finishes successfully and its metadata matches the scan snapshot.
- The queue does not automatically approve or export anything.

## Per-file limits

- Inventory: max 10,000 accepted files per scan.
- Image: <=80MP existing decoder limit; explicit model required.
- Video: 1fps sampling, existing FFmpeg service max 30,000 samples; memory bounded by single decoded frame + capped detections (<=60,000).
- GPU inference/priority scheduler not implemented in R4.0.
- Source symlinks/reparse points skipped; fixed output folder `AIPrivacyGuard_Output` excluded when nested under input root.

## Persistence

- Snapshot: `%LOCALAPPDATA%/AIPrivacyGuard/batch_queues/<hash of absolute input path>.json`.
- Image detections: existing `history.sqlite` tables.
- Video detections: existing `video_reviews/<file signature hash>.json`.
- No generated thumbnail files, raw images or video data persisted by batch code.
- Paths are sensitive: the snapshot directory should not be synced or published without user consent.

## Known ambiguities

- A completed 1fps video scan is not a validated whole-video safety check.
- Existing detection reviews are not invalidated automatically when an ONNX model changes; explicit reset allows a rerun while preserving review history.
- Only the first video stream in the source is scanned by the existing FFmpeg adapter.
- Supported codecs depend on local FFmpeg build.
