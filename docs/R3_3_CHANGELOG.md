# R3.3 changelog — conservative block-matching proposal (2026-11-09)

## Added

- `VideoBlockTracker.cs`: image-patch translation estimation using a bounded ±24px search on low-resolution BGRA samples, luma variability validation, ambiguity detection and forward/backward agreement. On a failed match, **stop** and report the timestamp and reason. This is **block matching, not optical flow**, a validated detector, or a privacy guarantee.
- `FfmpegVideoService.StreamTrackingSamplesAsync`: reads only a selected 0.3–15 second interval at 8 or 12fps (up to 640px long edge, at most 200 frames). Does not load the whole video into memory. FFmpeg input seeks and fixed-rate timestamps are *approximate*, not precise source PTS.
- WPF video panel: buttons for **generate motion proposal** and **explicitly apply proposal**, with 8/12fps selection; a successful pending proposal appears in purple versus saved orange mask, with enlarged proposed crop while scrubbing. CPU matching runs on a background thread. No existing mask is replaced before explicit consent.
- A successful applied proposal **always resets ReviewDecision to Pending**; it can only export after user inspection and approval. Incomplete proposals cannot be applied, and existing review metadata remains unchanged.
- Cancellation participates in existing stop button and disposes FFmpeg process on interruption.

## Evidence

- `python scripts/check_source.py`: PASS, XML/XAML references, **61 WPF handlers**.
- `python -m unittest discover -s tests/python -v`: **17/17 PASS**, including one FFmpeg synthetic-video segment sampling test, lossless video position verification and inherited moving-mask/output tests.
- Six C# xUnit tests added for translation, low texture, jumps, full interval, broken interval, range rejection. **Authored, not run:** no .NET SDK or Windows UI is installed in this environment.
- A separate temporary Python mathematical reproduction of the luma block-match search correctly matched synthetic shifts (+6,+4), (+3,0), (+3,+1) and rejected a large discontinuous jump. This does not execute the C# implementation.

## Important limitations

- No real target-trained exposure model, human subject tracking benchmark, or Windows app validation. No claims of recall, precision or live safety.
- If the candidate interval is longer than 15s, split it into smaller regions and inspect results.
- The tracker estimates translation only (no scaling/rotation/deformation/occlusion handling). It can produce wrong proposals when texture repeats, lighting changes, or movement is complex.
- The proposed trajectory is not stored until explicit apply. Even after apply, all points are unverified and the decision is reset to pending.
- FFmpeg sample time is approximate and sparse; interpolation may miss short-lived exposures. Never trust this as a privacy-safe broadcast output.
