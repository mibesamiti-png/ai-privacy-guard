# R7 — High-density offline video sampling (1/2/4/8/12 fps)

## Motivation

R6 enables a third-party adult-anatomy model, but the video scanner currently
samples at only 1, 2 or 4 fps. A brief event can occur entirely between
sampled timestamps. R7 provides higher cadence sampling **without claiming
frame-perfect video inspection**.

## Implementation

- Added `VideoScanPlanner` in Core with an estimated sample budget, nominal
  maximum interval, source frame rate warning and a hard **30,000 sample cap**.
- Existing Windows video UI scan dropdown now includes **8fps and 12fps**.
  Existing 2fps default and 1/2/4 modes are retained.
- `FfmpegVideoService.StreamSamplesAsync` accepts the new profiles and uses the
  same preflight policy. FFmpeg downsamples to <=640px long edge and streams
  BGRA frames, never loading the whole video.
- The video UI checks budget before starting inference, reports sample counts,
  nominal gap and warnings if the selected sampling rate exceeds the source fps.
- Existing event grouping, **Pending** review state, cancellation behavior and
  source hash review binding are unchanged.

## Example

| Selected scan rate | Nominal sample interval | Estimated samples on 60s source |
|---|---:|---:|
| 1 fps | ~1000ms | ~61 reserved |
| 2 fps | ~500ms | ~121 reserved |
| 4 fps | ~250ms | ~241 reserved |
| 8 fps | ~125ms | ~481 reserved |
| 12 fps | ~83.3ms | ~721 reserved |

Budget estimates reserve one potential extra boundary sample; actual FFmpeg
output count depends on duration and filter rounding. At 12fps, ~41min40s
approaches the 30,000-sample cap. A movie with fewer than 12 native fps
will repeat frames; higher output cadence cannot invent input information.

## Synthetic FFmpeg regression

The source stream consists of 30 frames of a black 64×64 image. Only frames
7–10 (about 133ms total) are white. The test encodes an FFV1 source
and uses the same FFmpeg `fps=...:start_time=0` sampling logic.

- At **1fps**, the short white pulse should not be sampled.
- At **12fps**, at least one sampled frame should be white.
- This is a *sampling cadence* check only; it **does not** validate model
  detection of underwear, breast exposure or any privacy incident.

## Warnings

- No finite sampling rate guarantees detection of an instantaneous event.
  A video-frame interval or VFR timestamp mismatch can still miss events.
- Model recall on anime, 3D games or fashion remains unvalidated.
- More samples use proportionally more CPU and time, and grouping/box tracking
  between sampled frames is still only a proposal, not verified tracking.
- This feature is **offline video analysis**, not the OBS live stream pipeline.
  OBS/renderer safety remains experimental; do not trust this for live privacy
  broadcasting.
- No user/private media or copyrighted game frames are committed to GitHub.

## Next R7 steps

1. Run full Windows CI, including Python/FFmpeg pulse demonstration and xUnit
   cap/spacing tests; publish updated Windows executable.
2. Run Windows WPF interactive smoke and a locally held consented adult sample
   clip, with human review of all proposed masks.
3. Consider range-limited high-density scan for long videos; base priorities on
   user interaction with the review queue instead of blind full-file scans.
