# R5.2 — Capture recovery, manual OBS audio offset, WPF demo launcher

## What changed

- **Capture self-recovery**: `LiveCaptureRecovery` reconnects after device disconnect, FFmpeg error or EOF, using bounded exponential retry delays (starting 300 ms, capped at 5 seconds). It always calls watchdog blackout before reconnect, during retries, and on shutdown. Every resumed frame gets a globally monotonic gate sequence even if the camera source resets frame numbering.
- **Stalled camera reads**: DirectShow FFmpeg reader cancels a read when a complete frame is unavailable for 3 seconds, kills FFmpeg and lets recovery re-create the process.
- **Fail-closed model faults**: model inference exception, bad result or timeout latches the gate fault and **does not automatically restart the model**. The server stays alive returning BLACK so the Browser Source can continue displaying blackout.
- **OBS Browser Source recovery**: the existing local Browser Source polls continuously, retries after endpoint/frame errors, and uses the watchdog to black out stale frames. No raw camera source is returned by its HTTP endpoint.
- **Manual OBS audio sync offset**: `ObsAudioSyncClient` optionally calls obs-websocket v5 `SetInputAudioSyncOffset` for a selected named audio input. Authentication uses an ephemeral environment variable, not a CLI password. It is a manual user-selected offset, **not** automatic audio-video sync or audio capture/mixing.
- **Main app integration**: a minimal R5.2 experimental tab starts/stops the **synthetic demo only** and copies its private localhost URL. Real camera mode remains a command-line experiment. Windows CI bundles the standalone LiveBridge inside the normal WPF app ZIP under `live-bridge/`.

## Running from the main app

1. Download the R5.2 **main Windows x64 artifact** from a green main CI, extract the entire ZIP.
2. Open `AIPrivacyGuard.exe` and select `OBS連携（実験）`.
3. Press `合成映像デモを起動`; copy the displayed Browser Source URL to OBS; use 640×360 resolution.
4. Press Stop or close the app; the bridge is terminated. On localhost request failure the browser page should paint black, but renderer crashes/hangs can still freeze the last visible frame.

## Command-line recovery test

Run the standalone `AIPrivacyGuard.LiveBridge.exe` extracted alongside its DLLs:

```powershell
.\AIPrivacyGuard.LiveBridge.exe --demo
```

An experimental camera without a compatible validated model **remains black**:

```powershell
.\AIPrivacyGuard.LiveBridge.exe --camera "Your Device Name"
```

If the device is unplugged, reconnect logic retries in the background and keeps the watchdog black. Do not use real footage to prove censorship; use a non-sensitive consented scene.

## Opt-in manual audio offset

OBS 5.x supports setting `SetInputAudioSyncOffset` through its local WebSocket. Set up OBS websocket locally, enable authentication, then supply the password via the `APG_OBS_WS_PASSWORD` environment variable **on your own machine** (never commit or paste credentials in GitHub). For example:

```powershell
.\AIPrivacyGuard.LiveBridge.exe --demo --obs-audio-input "Mic/Aux" --obs-audio-offset-ms 350
```

This adjusts OBS's named audio input by +350 ms. Allowed range is -950 to 20000 ms (OBS protocol). If OBS is unavailable or rejects the request, the video bridge still starts, prints a generic diagnostic, and the offset is unchanged. There is no auto-estimation; measure calibration against a known test clap and adjust manually. This does not modify or process audio packets and does not guarantee synchronization.

## Safety and verified/not verified

- These are **experimental transport and capture recovery tools**, not a reliable real-time privacy filter.
- Dedicated correctly licensed and measured exposure-detection AI is **not bundled**.
- Missing detection -> original frame can still be shown despite successful inference. Blackout handles transport faults only.
- OBS might bypass the bridge if the raw camera was added to a different scene/source. Verify OBS scene routing.
- If the OBS renderer or bridge process freezes/crashes, the browser fallback may not run. Hardware failure simulation remains mandatory.
- **Automated tests** cover synthetic frame capture reconnect, monotonic numbering, blackout transitions, model faults, audio websocket request validation, and WPF compile. Never claim real OBS/hardware/audio sync calibration unless manually observed.
- Public repository: do not upload private media, screenshots with identifying content, user review databases or local passwords.

## R5.3 next

1. Perform full Windows synthetic OBS smoke and camera disconnect/replug tests.
2. Add precise end-to-end video/audio latency measurement and optional calibration UI; avoid blindly applying offsets.
3. Device selection, process-recovery behavior on OBS renderer crashes, output heartbeat independent of page scripts.
4. Evaluate a suitable exposure detector on approved labeled data and fail-safe operating limits before attempting real live use.

## Verified R5.2 PR build / merge (2026-11-10)
- GitHub PR #7 merged to main as `114003647c5b24c38803602234fab48695cb390d`.
- [PR Windows CI #42677140187](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42677140187) passed: **103/103** core xUnit and **7/7** OBS HTTP/audio protocol tests; main WPF and LiveBridge EXEs compiled, published and artifact-uploaded.
- [Integrated main CI #42677559049](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42677559049) must be checked separately before claiming main PASS.
- Not validated: real DirectShow replugging, OBS desktop renderer recovery, A/V measurement/calibration, exposure-model accuracy.
