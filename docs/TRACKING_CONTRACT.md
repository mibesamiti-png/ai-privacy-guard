# Tracking contract (R3.3)

## Inputs

- Source video file selected locally; 0.3–15s time range already represented by a video review candidate.
- Valid normalized rectangle at the candidate start.
- `FFmpeg` segment sampler at 8 or 12fps, max long edge 640px, up to 200 samples.

## Outputs

- On success: a **proposed** sorted list of `(timeUs, normalized rect)` and an average image-match quality measure (not a calibrated probability).
- On failure: a break timestamp, reason code (`low_texture`, `poor_image_match`, `ambiguous_match`, `forward_backward_disagreement`, `incomplete_interval`, etc.) and the partial track **not eligible for direct application**.
- Existing candidate remains unchanged until user explicitly confirms; application replaces keyframes and resets decision to pending.

## Algorithm

1. Capture a 14×14 grid of luma samples from the padded reference rectangle.
2. Reject low-variance image patches and very small regions.
3. Search translation ±24 source pixels in both axes with coarse 2px step, then full-pixel refinement.
4. Reject high average luma difference or a similarly plausible spatially distinct competing match.
5. Run backward search and reject displacements that do not agree within 3px.
6. Stop at first failure. Never extrapolate beyond an untrusted match as if continuous tracking succeeded.

## Safety/UI

- Proposed and saved rectangles have different colors (purple/orange); the crop preview follows proposed rectangles while scrubbing.
- No implicit mask approval, no change to existing source video. Approved export remains user controlled.
- The real export is generated only from approved candidates, not from displayed proposals.
- The matcher is only a positioning aid; **does not detect skin, nudity or sensitive exposure**. The user must manually review the full output.

## Engineering constraints

- No Python/OpenCV runtime dependency for the Windows app; the C# matcher is self-contained in Core.
- Long or invalid intervals are rejected rather than partially accepted.
- The FFmpeg sampler is cancellable and disposes external processes.
- Exact PTS, optical flow, re-identification after occlusion and certified fail-closed live output are deferred work.
