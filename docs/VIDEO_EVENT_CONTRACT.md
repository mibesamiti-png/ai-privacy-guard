# R3 groundwork: video event grouping contract

**Status:** C# library module authored; not yet connected to a video decoder or GUI; no C# tests executed here.

`TimedDetection`:
- `TimestampUs`: nonnegative presentation timestamp in **microseconds**.
- `Category`: detector category ID.
- `Score`: finite range [0, 1].
- `Bounds`: valid nonzero box entirely inside [0,1] normalized coordinates.
- `TrackId`: optional tracker-assigned identity; no tracker is implemented yet.

`VideoEventGrouping.Group`:
- Orders detections by presentation timestamp; creates `VideoDetectionEvent` with `StartUs`, `EndUs`, and original observations.
- Merges only the same category and track ID (or both without track ID), with overlap at least `minIou`, strictly increasing presentation time and no time gap exceeding `maxGapUs` (default 250,000 µs).
- Does **not** merge two detections at the same timestamp, preventing collapse of distinct simultaneous objects.
- Does not modify the input or infer new detections between frames. If a candidate disappears for longer than the maximum gap, a new event begins.
- Is conservative: a fast-moving target may be split into separate events. This is preferable to accidentally combining unrelated regions in a human review queue.

## Follow-up for R3

- Decode video frames with FFmpeg and preserve original presentation timestamps and time base, including VFR.
- Sample or analyze frames and assign a consistent TrackId when a tested tracker becomes available.
- Group candidate observations and attach representative frames and optional crops.
- Review/approve per event, **then** apply masks to all relevant frames; do not infer coverage purely from grouped start and end times.
- Test gaps, irregular timestamps, scene cuts, motion, variable frame rates, duplicate frames, audio sync and export output.
