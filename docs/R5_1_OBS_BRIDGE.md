# R5.1 — Experimental OBS Browser Source + independent output watchdog

## What is implemented

1. **LiveOutputWatchdog** holds only gated `LiveFrameResult` frames and returns a fully opaque black frame:
   - before startup / before first usable detection,
   - after camera/inference stall exceeds the configured age,
   - if an error, blocked, or unprocessed result arrives,
   - when shut down or capture is lost, or
   - when frames arrive out of order.
   It operates on demand from the output HTTP server: it does **not** depend on the capture loop continuing to push frames.

2. **LocalObsBrowserSource** serves a tokenized local URL on `127.0.0.1`. Its only image endpoint returns **watchdog snapshots**, never raw camera frames. Its HTML client refreshes a BMP image about every 100 ms, and paints black if requests fail or stall. The endpoint is localhost-only and cache-disabled. The token is generated anew per process and is not stored in GitHub.

3. **AIPrivacyGuard.LiveBridge.exe** is a separate console prototype:
   - `--demo`: a synthetic animation and synthetic rectangle detector, suitable for a first OBS browser source smoke test **without any personal footage**.
   - `--camera "Device Name"`: experimental DirectShow input. **Without a compatible model, the gate is disarmed and outputs black only.**
   - `--camera "Device Name" --manifest model.json --experimental-unverified-model`: deliberately opt into experimental inference with a locally installed compatible ONNX model. Not suitable as proven protection; do not broadcast sensitive content using this unvalidated workflow.

4. Windows CI builds both executables; core and HTTP transport test suites execute on GitHub Actions. No new main WPF UI controls; user requested feature work before appearance refinements.

## OBS demo test (no real camera or private material)

1. Download the experimental LiveBridge Windows x64 artifact from GitHub Actions, and **extract all its files**.
2. Launch PowerShell in its extracted folder:
   ```powershell
   .\AIPrivacyGuard.LiveBridge.exe --demo
   ```
3. Copy the printed `http://127.0.0.1:PORT/TOKEN/` address.
4. In OBS, add a **Browser Source** with that URL. Set width to 640, height to 360.
5. Verify synthetic animation is displayed and a black mask covers part of it.
6. Stop the bridge or disconnect the source. The HTML renderer attempts to paint black upon request failure. **This does NOT guarantee OBS will not freeze its last frame if its browser renderer hangs or OBS is misconfigured.**

The LiveBridge EXE is independent from the main `AIPrivacyGuard.exe` and currently uses a console UI; this is an intentional early integration test.

## Safety and limits

- **Do not use this build for actual live privacy protection yet.**
- No licensed, evaluated nipple/underwear exposure model is provided.
- If any detection is missed and the detector returns an empty result, that frame may be displayed unmasked. There is no claim of 100% detection.
- OBS scenes containing the original camera source bypass this local bridge entirely.
- The watchdog can only respond when the LiveBridge process, HTTP endpoint, and OBS Browser renderer remain functioning; operating-system crashes and renderer freezes are outside this layer.
- Current BMP polling is intentionally bandwidth-intensive and a prototype; optimize encoding/frame delivery after transport reliability is established.
- The model's CPU inference latency, DirectShow camera support, audio synchronization, and actual OBS desktop operation are **not yet hardware-tested**.
- Do not commit local videos, thumbnails, crop samples, manifests containing sensitive paths, or personal review DB files to the public repository.

## Verification plan

- Automated unit checks: black before first frame, stale timeout, blocked/faulted frame, forced blackout, out-of-order delivery, buffer isolation, BMP header and alpha.
- HTTP integration check: authorized localhost Browser Source and image route work; unknown routes 404; output black → gated synthetic frame → black.
- Manual Windows smoke: start the synthetic demo; add browser source to OBS; stop processing and confirm black; restart; test camera disconnect with a consented non-sensitive scene; confirm neither OBS nor any other source has raw video routed around the gate.
- Only after these checks evaluate a suitably licensed, compatible exposure model on appropriate labeled data.

## Next milestone R5.2

Add more robust output watchdog/renderer failure monitoring, connection recovery, integration with the main app, explicit safe input-source inspection, audio synchronization and a model evaluation gate. Do not treat this R5.1 prototype as production-ready.

## Initial verified CI / release status
- PR #5 merged into main as `0c758a8b53cb9a36af93a5c6219a6a09d0f7e236`.
- PR Windows CI PASS: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42673993149
- Core C# tests: 98/98; localhost OBS Browser Source integration: 1/1; both EXEs published and artifacts uploaded.
- Main push CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42674405361 (confirm green before declaring final main verified).
- No end-to-end real OBS/camera or sensitive detection accuracy test has been completed.
