# Feature status — R4.3 (2026-11-09)

| Feature | Code state | Verification |
|---|---|---|
| R0–R1.1 image read/manual mask/zoom/SQLite | Authored | Windows C# compile and unit tests PASS; desktop UI NOT TESTED |
| R2 ONNX model adapter/diagnostics | Authored | Compiled; synthetic/mocked fixtures; real compatible exposure model NOT TESTED |
| R3 video seek/sample scan/motion interpolation | Authored | Compile/xUnit PASS; synthetic FFmpeg fixtures; desktop UI NOT TESTED |
| R4.0 batch scan/queue/resume | Authored | Compile/xUnit PASS; desktop UI NOT TESTED |
| R4.1 approved-only image/video batch export | Authored | Compile/xUnit PASS; FFmpeg synthetic tests PASS; desktop UI NOT TESTED |
| R4.2 image + sampled video before/after comparison | Authored | Compile/xUnit PASS; FFmpeg sample comparison PASS; desktop UI NOT TESTED |
| R4.2 SHA-256 bound video review / legacy reset | Authored | C# tests PASS; Python synthetic content-hash check PASS; desktop UI NOT TESTED |
| R4.2 sample audit timestamp | Authored | Compile/xUnit PASS; GUI/restart NOT TESTED |
| R4.3 Windows CI / executable artifact | Verified in CI | Main workflow PASS: build, 78 C# tests, 20 Python tests, publish, artifact upload |
| R5 live OBS/latency/fail closed | Not implemented | — |
| Dedicated validated exposure model | Not implemented | — |

`Implemented` or `compiled` does not imply reliable sensitive-content detection or operational safety. Full Windows WPF smoke testing and a suitable dedicated model are still required.

## R5.0 experimental core (2026-11-10)
- `LiveSafetyGate` and `LiveFramePipeline`: **code authored**, black-frame fallback tests added, Windows PR CI **PASS 89/89**; hardware/UI **NOT TESTED**.
- `DirectShowLiveCapture`: **code authored**, real Windows camera device run **NOT PERFORMED**.
- No OBS or virtual camera output, no persistent watchdog or validated sensitivity model; **not live-stream safe**.
- See `docs/R5_0_LIVE_CORE.md`.

## R5.1 OBS Browser Source prototype
- Core watchdog + BMP encoder: code integrated, C# tests PASS on GitHub Windows CI (98/98 core tests).
- Localhost sanitized-only Browser Source, generated URL token, HTML polling fallback: one Windows CI HTTP integration test PASS (1/1).
- Standalone executable with synthetic demo / experimental DirectShow: Windows publish artifact PASS, actual OBS desktop/camera smoke NOT TESTED.
- **Not production-ready.** No validated specialized exposure model, no completed real camera/OBS/manual end-to-end testing, no audio sync.
- Details: `docs/R5_1_OBS_BRIDGE.md`.

## R5.2 capture/OBS integration — PR CI passed
- Automatic camera reconnect with exponential backoff and blackout: **CI passed**, 103/103 core xUnit tests; real camera unplug NOT TESTED.
- Timeout on hung camera frame reads: **source authored**, physical device unplug test pending.
- OBS audio input manual sync offset via localhost WebSocket: **CI payload tests passed**; real OBS connection/sync calibration not tested.
- WPF experimental synthetic demo launcher with bundled bridge: **Windows WPF compile passed**, real desktop smoke pending.
- OBS renderer/process crash cannot be guaranteed to clear stale pixels; no dedicated verified exposure detector.
- See `docs/R5_2_RECOVERY_AUDIO.md`.

## R6 — NudeNet exposure AI integration (PR CI verified, merged)
- ONNX raw YOLOv8 18-class decoder and square-pad preprocessing: source added; tests PASS in 109/109 core suite.
- Local one-click third-party model download + pinned SHA-256 and offline reload: source added; Windows compile PASS, interactive GUI untested.
- Existing image/video/batch analyzer uses shared detector interface. No new auto-approval.
- Real NudeNet 320n ONNX downloaded with pinned SHA, loaded, and 2 synthetic CPU inferences PASS in GitHub Actions #42687318433. Real accuracy untested.
- Model is not bundled; NudeNet is **not** a dedicated skirt-underwear visibility model. No empirical accuracy metrics, NOT safe for live protection.
- See `docs/R6_NUDENET_MODEL.md`.

## R6 clothing-domain sensitivity test
- Real-image adult lingerie/swimwear/clothed control/2D/3D/cosplay **19/19 fetched and model-tested in CI #42707282951**.
- Threshold sweep 0.10/0.25/0.40: source metadata and detections are reproducible. User model default 0.25 **unchanged** because lower threshold added a possible dressed-body false positive.
- Specialized under-skirt/panty-visible detector: **NOT IMPLEMENTED**, no consenting adult positive ground-truth corpus; cannot claim real incident detection recall.
- See `docs/R6_LINGERIE_FASHION_DEBUG_RESULTS.md`.

## R7 high-density offline video scan (Windows PR CI verified)
- UI and FFmpeg streaming add 8/12fps to original 1/2/4fps rates; default remains 2fps.
- New `VideoScanPlanner` preflight caps 30,000 samples, shows nominal blind interval and warns on duplicate sampling above source fps.
- Synthetic 133ms pulse FFmpeg test PASSED (1fps missed, 12fps sampled). C# planner unit tests PASSED. PR #14 Windows CI #42710234669: 121/121 Core xUnit, 7/7 OBS tests, 21 Python tests, build and publish all green.
- No true complete-frame safety guarantee, no validated under-skirt-specific model, not for live broadcasting.

## R8.0 — independent full-frame multi-model proposals (2026-11-10)
- `MultiModelProposalAnalyzer` is merged to Core via PR #16. It preserves separate model ID, category, confidence and rectangle; does not rely on NudeNet's ROIs, deduplicate across different models, or automatically approve/mask/export.
- **Verified on PR Windows CI #42715218212**: 129/129 Core xUnit, 7/7 OBS xUnit and 21 Python/FFmpeg tests. New synthetic tests cover empty primary vs specialist proposals, provenance, cancellation, invalid output and failure handling.
- **NOT IMPLEMENTED**: a real specialist garment model, image/video/OBS UI integration, genuine positive recall measurement, live stream protection. A completed inference does not imply a safe image. See `docs/R8_MULTIMODEL_PROPOSALS.md`.

## R8.1 experimental standalone zero-shot garment research (2026-11-10)
- **Verified model execution** in research workflow #42721586938: both Grounding DINO Tiny and OWLv2 loaded and inferred on one 256×256 non-human synthetic garment silhouette. 11 vs 0 **candidate boxes**, uncalibrated, overlapping and no independent ground truth. About 29.8s vs 53.3s CPU inference, respectively.
- **Verified ordinary Windows PR workflow #42721585795**: 129 Core, 7 OBS, 30 Python integration tests PASS; app/bridge still publish. Added 9 stdlib-only Python comparison tests.
- **Not yet integrated or measured**: a validated specialist trained on any consenting adult positive examples, still-image/3D/anime recall, prompt calibration, model snapshot immutability, UI, or live latency/safety. R8.1 is a separate Python research tool only; Windows WPF remains R7 user-facing. See `docs/R8_1_ZERO_SHOT_COMPARISON.md`.

## R8.2 region label consistency study (2026-11-10)
- Code authored on PR #20: IoU-based grouping of R8.1 clothing candidate boxes; keeps contradictory labels rather than forcing a winner. Context-padded crop ranked by independent pretrained LAION CLIP model, **research only**.
- Dedicated research CI #42826965515 PASS: 10 tests, one genuine model inference on non-human synthetic shape. Source garment labels [skirt, dress, underwear] collapsed to one review group; 167.69ms CPU crop classification. All cases remain review-required and safety unknown.
- Does NOT provide real dedicated accidental-exposure model, reliable anime/3D/photo recall, trained small specialist, calibrated scores, real-time performance or live OBS safety. Existing Windows WPF behavior not changed.

## R8.3 Validation Lab (specification only, not yet shipped)
- Planned local first: persistent human-labeled image queue, video playback review with PTS-specific overlays, missed-interval annotations and honest scanned/unscanned coverage, then quantitative independent ground truth evaluation.
- Optional X gallery to search public media by account/keyword via approved official API after explicit consent and cost control; media viewing is not permission to store/train on it.
- Full specification and phase gate tests: `docs/R8_3_VALIDATION_LAB_ROADMAP.md`. Existing application features are **not** changed by this design PR.

## R8.3-A + E0 (PR #23, Windows CI verified)
- New `ValidationLabStore` in Core (local file integrity, negative image vs unknown vs uncertain, video positive region timestamps, persistent undo) and new WPF Validation Lab tab with rights-checked local media, ordinary manual X browser via WebView2, permalink bookmarking. Notes at `docs/R8_3A_LAB_BROWSER_MVP.md`.
- No X paid API calls, no media scraping/downloading, no automatic exposure accuracy claims. Existing WPF video detector and OBS unchanged. Next: human positive box/UI, image gallery, frame-time video review.

## R8.3-B — implemented on PR #24, preliminary Windows CI PASS
- Interactive local-image rapid review tab, review hotkeys, original-image overlays for model proposals vs manually confirmed regions, local detection using already selected ONNX detector, rapid queue with local thumbnails, multi-file explicit-rights import.
- New human candidate verdicts (correct/false positive/uncertain) are in the independent ground-truth store; they do **not** modify masking approvals and do **not** automatically certify whole images or video as safe. Human misses can be drawn as positive regions.
- First Windows CI #42861165081 passed 160 Core / 7 OBS / 40 Python (4 conditional skips) and WPF published. Final UI navigation patch and Windows interactive GUI testing remain separately qualified. See `docs/R8_3B_IMAGE_RAPID_REVIEW.md`.
- In-app video player with PTS timeline, frame stepping and playback-time model overlays **NOT IMPLEMENTED** (next R8.3-C).
