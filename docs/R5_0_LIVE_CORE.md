# R5.0 — Experimental live-frame safety core (2026-11-10)

## Scope
This round adds **headless primitives**, not a finished or usable live broadcasting feature.

- `LiveSafetyGate`: experimental per-frame detection and masking with **opaque black frames** when disarmed, missing a detector, timed out, invalid output, stale/busy frame, cancellation, or detector exception.
- `LiveFramePipeline`: routes frames from input to a sink **only after** processing them through the gate.
- `DirectShowLiveCapture`: reads camera video on Windows through FFmpeg DirectShow into bounded 16–1920 × 16–1080 BGRA frames, with cancellation and child process cleanup.
- Added 11 synthetic xUnit tests. CI will verify compile and test results on a Windows runner.

## Safety model
- **Defaults to disarmed**, including when no model is configured. A deliberate `EnableExperimentalProcessing()` call is required.
- A timeout or model error **latches the gate faulted**. The caller must construct a new gate after diagnosis; there is no automatic unsafe bypass.
- Blackout results are fully opaque BGRA black. Original buffers are not altered by the gate.
- Empty detection results are labeled **Screened**, **not safe**. A detector that misses exposures may still output unmodified pixels. No validated sensitive-exposure model has been bundled.
- If the user disables processing during an in-flight inference, that frame is blocked.
- Camera frames are raw only *inside* the input adapter; never expose the raw DirectShow stream to OBS or another output.
- **No real-time OBS output, audio sync, always-on watchdog, stream buffering, or capture device UI is implemented.**
- An interruption upstream of the pipeline still needs a downstream black-frame watchdog before any broadcast use.
- Do **not** use R5.0 to prevent exposure in production live broadcasts.

## Next R5.1+ milestones
1. Test real Windows DirectShow device discovery and frame capture using a synthetic/consented camera scene.
2. Add a long-lived output watchdog that emits black frames even if input capture stalls or crashes (as opposed to the per-frame gate alone).
3. Implement a *sanitized-only* virtual camera/OBS interface with no raw-capture passthrough.
4. Measure camera and model latency; use a bounded buffer/backpressure policy, timestamp alignment and optional audio delay.
5. Integrate a validated appropriately licensed exposure detection model and test measured false-negative rates. Do not claim 100% reliability.
6. Complete human review, UI controls, stream failure simulation and an explicit go/no-go safety checklist.

## CI and verification
- GitHub PR branch: `feature/r5-live-safety-core`.
- This document describes code intent; mark CI/Windows smoke **PASS** only after checking actual workflow jobs and logs.
- No private footage, thumbnails or personal data should be committed to this public repository.
