# AI Privacy Guard R4.3 — GitHub handoff (2026-11-09)

## Current source of truth
- Repository: https://github.com/mibesamiti-png/ai-privacy-guard (Public; never commit private/user media or personal review databases).
- Main HEAD after R4.3 build fixes: `d77d7c2080167c7bd662713064cb8fb38de3cca5`.
- PR #1 merged: https://github.com/mibesamiti-png/ai-privacy-guard/pull/1
- **Main Windows CI PASS**: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651
- Build artifact: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42666438651/artifacts/13696203027

## Verified
- Windows x64/.NET 10 full solution build succeeded.
- All 78 xUnit tests passed on the PR head; main's build/test/publish stages also passed.
- Python/FFmpeg synthetic integration suite: 20 tests passed.
- Windows publish directory uploaded as an artifact containing the executable.

## NOT verified
- EXE starts, WPF window and event handlers work on a user's desktop; no interactive smoke test has been performed.
- End-to-end manual masking, export, batch resume and before/after compare in the actual Windows UI.
- Compatible specialized exposure-detection model, inference quality and recall (model not bundled).
- Safe real-time OBS capture/output (R5 not implemented); DO NOT use for live exposure-prevention yet.

## Highest priority next steps
1. Download the main build artifact, extract the full directory and perform `docs/SMOKE_TEST_CHECKLIST.md` with **synthetic** test files.
2. Fix WPF launch/runtime issues found by those tests and rerun CI; record outcomes and logs in `docs/TEST_RESULTS.md`.
3. Run an offline end-to-end image/video/batch test, including Pending export blocked and SHA-256 review invalidation on modified source.
4. Measure high-resolution hashing, FFmpeg process cancellation, long-video memory, VFR timestamps and mask tracking failure handling.
5. Evaluate a legitimately licensed compatible model on an approved labeled dataset before claiming sensitive-exposure detection capabilities.
6. Only after Windows application workflows and accuracy are reviewed: start R5 with a fail-closed output design.

## Build process
- Branch changes via PR; CI on PR and main push.
- Required green steps: source checks → Python/FFmpeg → restore → compile → xUnit → publish → artifact.
- A published EXE file is **not** equivalent to a tested WPF application.
- See `docs/R4_3_CHANGELOG.md`, `docs/FEATURE_STATUS.md` and `docs/SMOKE_TEST_CHECKLIST.md`.

## New R5.0 development branch (2026-11-10)
- Branch: `feature/r5-live-safety-core` **merged** via PR #3; main commit `a67133657f0a662dd04cd04bd543c71a31d26231`.
- Added experimental black-frame `LiveSafetyGate`, sanitized-only `LiveFramePipeline`, and Windows DirectShow capture adapter.
- Synthetic xUnit tests: **89/89 passed** in PR CI [#42670038848](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670038848). Full Windows camera hardware and UI integration **NOT TESTED**.
- R5.0 remains **headless and not ready for broadcast**: no output/watchdog/OBS and no validated exposure detection model.
- Next action: verify main CI [#42670458131](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42670458131), then implement downstream watchdog and sanitized output. See `docs/R5_0_LIVE_CORE.md`.

## R5.1 implemented and merged, CI verified
- Merged PR #5 into main, commit `0c758a8b53cb9a36af93a5c6219a6a09d0f7e236`.
- Adds `LiveOutputWatchdog`, `BmpFrameEncoder`, `LocalObsBrowserSource`, and `AIPrivacyGuard.LiveBridge` standalone console.
- OBS integration is a localhost Browser Source that receives only watchdog snapshots; separate from main WPF UI. Synthetic `--demo` needs no camera/model.
- New core watchdog tests and localhost HTTP integration test: Windows CI passed, **98/98 core tests, 1/1 browser test**. Both executable artifacts uploaded.
- Read `docs/R5_1_OBS_BRIDGE.md` before changing architecture. No personal media in public GitHub.
- Next action: verify the integrated `main` CI, run synthetic Windows/OBS manual smoke, then test real camera disconnect using non-sensitive footage; do not use for production live privacy.

## R5.2 ongoing development
- PR #7 merged into main as `114003647c5b24c38803602234fab48695cb390d`; PR Windows CI passed.
- Capture recovery and bounded camera read timeout; gate numbering preserved across reconnects, model faults stop processing.
- Optional **manual** audio offset through OBS WebSocket, password from `APG_OBS_WS_PASSWORD` only; not automatic synchronization.
- New WPF synthetic OBS demo tab starts bundled `live-bridge/AIPrivacyGuard.LiveBridge.exe`, does not expose unverified camera launch as one-click operation.
- CI now bundles independent LiveBridge in normal Windows artifact. Both executables published and WPF compiled; 103/103 core C# tests and 7/7 OBS transport tests PASSED on GitHub Windows CI #42677140187.
- Integrated main CI: https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42677559049 (verify final status before claiming success).
- Details: `docs/R5_2_RECOVERY_AUDIO.md`. All real hardware/OBS testing still requires local Windows.

## R6 NudeNet AI integration — merged and verified PR
- PR #9 merged in main commit `69d095ad87fd2e3110237633e7ee40eee21ac5b4`.
- New NudeNet YOLOv8 18-class raw output decoder, 320n-specific zero-square preprocessing, manifest support, pinned URL/hash and local installer.
- Minimal GUI button in Image tab; installed model auto-loads offline and powers existing image/video/batch inference.
- Real pinned 320n ONNX CPU smoke PASS on synthetic frames, GitHub Actions #42687318433. Weights were temporary and not bundled.
- CI PASS: core xUnit 109/109; OBS tests 7/7; real ONNX 2/2 synthetic inferences. Next: verify main CI #42687745033, then perform local image/video detection quality evaluation. Model recall still unknown.
- Model weights are NOT committed. Read `docs/R6_NUDENET_MODEL.md` and note AGPL/relicensing ambiguity before redistribution.

## R6 cross-domain ONNX evaluation — 2026-11-09
- PR #12 adds reproducible ephemeral adult runway/cosplay, 2D Yor/Ningguang and 3D Raiden/Ningguang image probes.
- CI #42694286982: **7/7 public sources loaded, 1 total candidate**, `FEMALE_GENITALIA_COVERED` (confidence 0.665824) on an adult swimwear runway photo. 2D, 3D and cosplay clothed controls: 0 candidates.
- Raw third-party image pixels deleted before publishing the GitHub artifact; report holds links/attribution/hashes, class boxes and geometry-only maps.
- NO true under-skirt positive validation; see `docs/R6_REALMEDIA_EVAL_RESULTS.md`.

## R6 adult underwear / cosplay / Genshin threshold debug
- PR #13 adds reproducible on-runner real NudeNet inference on **19/19** adult fashion, lingerie, swimwear, convention cosplay, 2D Yor/Ningguang and 3D Raiden/Ningguang sources.
- Windows CI #42707282951 **SUCCESS**, per-source fetch & ONNX sweep actually passed. 109/109 core tests and 7/7 OBS tests still passed.
- At thresholds 0.10 / 0.25 / 0.40: respectively 5 / 2 / 1 images emitted candidates (6 / 2 / 1 boxes).
- Among five lingerie-referenced images, any detections 2/5, 1/5, 0/5. **Not panty-shot recall**: no labeled positive under-skirt incident samples.
- Lowering 0.25->0.10 also emitted a possible false positive `FEMALE_BREAST_EXPOSED` on a black dress photo; **do not auto-lower shipping threshold**.
- No source person/game imagery in public artifact; source URLs and abstract boxes only. See `docs/R6_LINGERIE_FASHION_DEBUG_RESULTS.md`.
- Next: build a dedicated annotated adult garment visibility dataset/second-stage classifier with positive/negative cases and explicit privacy-scene context.

## R7 dense offline video scan — verified PR, merged
- PR #14 merged into `main` as `77b5c15900350a290ca5d4b25803fcf106692131`. Video scan cadence now 1/2/4/**8/12** fps, with preflight 30,000-sample cap, estimated counts, and warnings when source fps is lower than selected cadence.
- Python FFmpeg regression uses **only synthetic video**, 133ms pulse: intended to demonstrate 12fps can see an event 1fps misses. C# xUnit tests cover budgeting and invalid configurations.
- No change to live OBS streaming; not evidence of model recall or complete frame coverage. No private media committed.
- Verify green Windows CI and actual pulse logs before merge. See `docs/R7_HIGH_DENSITY_VIDEO_SCAN.md`.

## R8.0 independent multi-model proposals — merged, Windows PR CI verified (2026-11-10)
- PR #16 merged into main at `78c34660580504bd63fb70bb31a3f66f4f54fb6f`: https://github.com/mibesamiti-png/ai-privacy-guard/pull/16
- Windows CI #42715218212 PASS: **129/129 Core xUnit, 7/7 OBS xUnit, 21 Python/FFmpeg tests**, real NudeNet ONNX smoke and external media scripts, Windows WPF/LiveBridge compilation/publish. Interactive Windows UI still NOT TESTED.
- New `MultiModelProposalAnalyzer.ScanAsync` independently runs supplied `IExposureDetector` models on the full original frame. A future specialist can propose its own boxes even when NudeNet returns zero. Proposals retain model provenance and separate scores; model failures explicitly mark an incomplete scan.
- This is **a Core API foundation, not a trained garment specialist**. No specialist model, UI/OBS wiring, trained accuracy or proven privacy safety exists. R7 user-facing behavior remains unchanged.
- Next: R8.1 independently annotated consented adult 2D/3D/real positive+negative benchmark; R8.2 actual specialist model integration; R8.3 held-out precision/recall and runtime evaluation; R8.4 opt-in WPF review integration. Full scope: `docs/R8_MULTIMODEL_PROPOSALS.md`.

## R8.1 zero-shot garment probe — PR #18 merged, two CI workflows verified
- Main merge commit: `948f17c2feba69cbfa25aa4b811cb9302689f31a`, https://github.com/mibesamiti-png/ai-privacy-guard/pull/18.
- Windows PR CI https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42721585795 **SUCCESS**: 129/129 Core C#, 7/7 OBS C#, **30/30 Python/FFmpeg tests**, Windows artifacts published. Real-model research CI https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42721586938 **SUCCESS**, actual Hugging Face checkpoints, not stubs.
- One original **synthetic geometric, non-human dress silhouette**, 256×256, research threshold 0.15. Grounding DINO Tiny: load ~10.58s, inference ~29.82s, **11 overlapping class-prompt-dependent proposals** (not 11 distinct true objects). OWLv2: load ~25.83s, inference ~53.25s, **0 proposals**.
- Candidate types include conflicting `shorts`, `underwear`, `skirt`, `swimsuit`, `dress` for the SAME region. Thus class discrimination is not validated. A single nonhuman image cannot establish precision/recall, 2D/3D suitability, licensed adult privacy-incident recall or live performance. CPU timing suggests neither unoptimized checkpoint belongs in real-time OBS.
- Research-only Python script `scripts/r8_zero_shot_compare.py`; actual smoke workflow `.github/workflows/r8-zero-shot-research.yml`. JSON-only artifact contains no images or weights. Model weights downloaded only in experiment runner. WPF app stays at R7 UI capabilities; R8.0 Core not wired into WPF/OBS.
- Important troubleshooting: the research workflow initially had a job-invalid `runner.temp` expression at job env level, then duplicate Transformers `trust_remote_code` kwarg, then missing SciPy for OWLv2; all corrected and final two-model smoke verified. Keep final successful run as source of truth.
- **Next:** R8.2 evaluate a fast specialized garment/occlusion classifier with authorized adult labeled positive/negative data and domain splits. Compare recall/FP and runtime against these baselines; no automatic masks/live protection until evidence exists.

## R8.2 research handoff (2026-11-10)
- PR #20: https://github.com/mibesamiti-png/ai-privacy-guard/pull/20 . Adds `scripts/r8_2_region_reviewer.py` (IoU cluster/conflict retention and independent padded region crop classifier), ten Python tests, dedicated synthetic research workflow, and `docs/R8_2_REGION_CONSISTENCY.md`. No WPF/OBS runtime integration or automatic clearance.
- Research CI #42826965515 **SUCCESS**: real LAION CLIP ViT-B/32 safetensors CPU inference, 3 intentionally conflicting synthetic labels -> 1 review region, 167.69 ms classifier inference, 4.58s model load; top relative category `skirt` 0.73019. **Not calibrated, trained, or a privacy/exposure recall result.** Only a non-human 256x256 original silhouette was analyzed.
- Current development work remains experimental. Next R8.3: held-out consented adult + non-human 2D/3D/photo domain benchmark (group-safe splits, genuine positives and negatives); small specialist training/quantization; ONNX only if validated, and manual review still required. Do not commit images, weights, credentials, source paths or private review data.

## R8.3 Validation Lab design — 2026-11-10, PLAN ONLY
- User requested a fast manual-correction image gallery, video playback-time detection inspection and missing-event annotation, and optional X (Twitter) user/keyword image/video discovery. Design is documented at `docs/R8_3_VALIDATION_LAB_ROADMAP.md`; **none of its UI/API/storage code has yet been implemented**.
- Priorities: A local annotation schema/source integrity and undo -> B image review hotkeys -> C timestamp-synchronized video playback evaluation -> D leak-safe benchmark -> E opt-in, rate/cost-bounded compliant X discovery; do not make external API access a prerequisite for local review.
- R7 existing offline 1/2/4/8/12 fps video sampling is not frame-perfect; playback detection must distinguish sampled/unscanned frames and avoid stale overlays. Current research CLIP/Grounding DINO prototypes remain offline-only.
- For X, rely on current official API plans and policy, do not automatically download private/restricted media, bypass logins or treat publicly visible content as licensed benchmark/training material. Test with mocked API and synthetic media before user-authorized real API use.

## R8.3 X discovery preference update (2026-11-11; DESIGN ONLY)
- User prefers **no paid X API for the default discovery workflow**. Use a plain WebView2 browsing panel (or external browser fallback), manual keyword/user search, X's native images/videos result tabs, and explicit bookmark of the original X post permalink to a private local annotation project.
- **Do not** scrape/automate X website, extract media via hidden requests or cookies, auto-download bulk imagery, or assume a public post grants rights for training. X browser media cannot be assumed locally decodable by the app: require independently approved user-provided local source files for actual pixel/video frame inference.
- Default X API usage **0 calls, $0 API fee**. Optional paid official X API structured gallery is deferred. Updated source of truth: `docs/R8_3_VALIDATION_LAB_ROADMAP.md` section R8.3-E0. Not yet implemented or desktop-tested.
