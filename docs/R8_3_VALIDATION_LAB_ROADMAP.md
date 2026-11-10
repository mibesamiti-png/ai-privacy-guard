# R8.3 — Human-in-the-loop Validation Lab (image, video, X discovery)

**2026-11-10 design proposal, not implemented.** This document specifies the *next* development work for the AI Privacy Guard Windows WPF application. None of the UI, X connectivity, annotation store, or live video-playback validation features below are implemented merely by merging this document.

## Motivation and verified baseline

- R7: Offline video scanner samples 1/2/4/8/12 fps (2 fps default), 30,000-sample cap, independent FFmpeg decoding. A scan is **not** guaranteed every-frame coverage. See R7_HIGH_DENSITY_VIDEO_SCAN.md.
- R8.0: Independent full-frame proposal collection, **Core API only** and not wired to WPF or OBS.
- R8.1: Independent pretrained Grounding DINO Tiny / OWLv2 proposal research on **one non-human geometric garment image**. Candidate categories conflicted; no annotated ground truth.
- R8.2: Experimental region grouping and separate CLIP crop-ranking *research runner* on **one non-human geometric garment image**. This does not provide calibrated exposure classification, production latency, or reliable safety decisions.
- The WPF app has existing image/video review and mask/export mechanics, but **the dedicated annotation and X-source collection workflow specified here is new**. Existing export review decisions must not be conflated with objective ground-truth labels.

**Primary outcome:** A reviewer can build small, reliable, rights-cleared, local-only positive and negative validation sets with image and timestamp-level video annotations, measure actual false negatives, and compare model revisions without exporting source imagery.

## User experience — one Validation Lab entry

A single "検証ラボ" tab with a source switch:
1. "ローカル画像" — folders and files, thumbnail grid, next/unreviewed/false-negative queue.
2. "ローカル動画" — playback, annotation timeline, frame step, region selection and bookmarkable timestamps.
3. "Xで探す" — optional, explicitly opt-in discovery of publicly accessible posts with image/video filter; **not bulk downloading or unauthorized model-training import**.

Shared right panel: detector model/version, proposal list + rectangle overlay, confidence **as raw model score, not probability**, "正解候補 / 誤検出 / 見逃し追加 / 判断保留 / 対象外", previous/next, undo, autosave state, original source attribution and review progress.

Recommended shortcuts: \`1\` candidate correct, \`2\` false positive, \`3\` add missed region, \`4\` uncertain, \`5\` not applicable, \`N\` next unreviewed, \`P\` previous, \`Ctrl+Z\` undo. Prevent shortcuts while typing; never silently label an entire unchecked frame as "negative".

## Work plan and acceptance criteria

### R8.3-A — AnnotationCore & local project store (MUST FIRST)

- Standalone annotation types with stable IDs: \`SourceAsset\` (local source reference, media type, visual domain: 2D/3D/photo, author/copyright provenance, rights/consent eligibility, immutable source hash), \`ReviewFrame\` (presentation timestamp microseconds for video; sampler status), \`TruthRegion\` (normalized rectangle, target label, frame/time interval, uncertainty, reviewer decision), \`ProposalAssessment\` (model ID/version, candidate ID, matched truth ID, TP/FP/ignored/uncertain), \`ReviewSession\` and revision history.
- Separate \`review_status = unreviewed | partial | fully_reviewed | excluded\` from \`ground_truth = positive | negative | uncertain\`. **Unreviewed/unscanned is never negative.** Explicitly inspected no-positive frames only become negative by reviewer action.
- Target definition is versioned. Do NOT merge "garment found", "specific clothing item", "occlusion/visibility event", and "actual exposure incident" into one ambiguous label. Keep "visible vs occluded" plus context/scene as distinct annotations; "unknown" permitted.
- Rights/source classification: user-owned or explicit permitted adult-only licensed images and nonhuman original fixtures for testing, with evidence kept privately. Any ambiguous age/rights content excluded from sensitive ground truth. Do not infer consent from a public post alone.
- Store inside a local per-project directory with SQLite or atomic versioned JSON transaction, append-only undo/audit event trail, recoverable backups, no photo bytes in public repo or CI. Source SHA-256 and metadata change invalidates prior labels until re-link/review. All encryption, if offered, is explicit and tested; no false "fully encrypted" claim.
- Acceptance: save/reload/undo/redo, schema migration, source mismatch, duplicate media, offline startup, malformed project, corrupt-store recovery tested with synthetic fixtures; private media paths and secrets never printed in diagnostics/CI.

### R8.3-B — Image rapid review

- Thumbnail grid and filters (unreviewed, TP candidate, false positive, missed-positive, uncertain, domain 2D/3D/photo), sorting, duplicate-aware import.
- Show original, detector overlays, label conflicts, low-confidence/failed model status; allow drag-to-create a missed region and resize/correct existing boxes. Warn against cross-model score comparison.
- Key-driven human labeling and next-unreviewed progression; autosave with explicit persisted indicator; batch operations require an explicit confirmation and must never auto-mark negatives.
- Acceptance: synthetic 20-image queue, close/reopen without loss, 10 edits then undo, duplicate detection and missing-file re-link, source hash mismatch, no silent accuracy claim.

### R8.3-C — **Video playback + live inspection overlay** (high priority)

**Two modes with different guarantees, both useful:**
1. \`再生しながらチェック\`: keep decoded video/audio playback responsive. Queue model work asynchronously with bounded 1–2-frame inference queue and configurable sample cadence/CPU budget. Overlay a detection only on its matching source presentation timestamp, never on the wrong displayed frame. If inference falls behind, mark that span \`not_scanned\` / \`delayed\` and skip/stale-drop *inferences*, **not** playback; show scan coverage and measured per-frame delay. When inference is too slow, suggest offline pre-scan automatically, without pretending this is live protection.
2. \`先に解析してから再生\`: run the existing R7 offline 1/2/4/8/12 fps scanner, then view synchronized proposal boxes while playing. Explicitly show which video frames/timestamps were sampled and which were not.

Video controls: pause, 1-frame forward/back, seek timeline, 0.25×/0.5×/1× playback, repeat a short selected time range, \`M\` at a time to add a missed candidate, time interval brush, editable box, and event bookmark list with thumbnails generated **locally only**.

Review timeline lanes (grounded by decoder PTS rather than guessed wall-clock positions): \`sampled/no proposal\`, \`proposal\`, \`human-confirmed event\`, \`manually marked miss\`, \`unscanned\`, \`decode/model fault\`. Distinguish live playback analysis FPS from source FPS and offline sampling FPS, and record actual counts.

Important: precise frame-by-frame review should step *decoded source frames* not replicate synthetic sampling timestamps. Respect VFR presentation timestamps, seek discontinuities, audio sync, scene cuts, model stalls, repeated frames, cancellation and non-monotonic/duplicate packet timing. Decoder stream must not be blocked by large CLIP/Grounding DINO model; initially default to verified fast existing model and label research models as OFFLINE-only.

Acceptance: synthetic 30fps video with 4-frame (~133ms) positive visual flash and a VFR fixture; 2fps scan may miss it, 12fps likely sees sampled flash but model result must not be faked. Human can mark a missed incident via frame-step even when the model is silent. Reopen restores exact presentation-time annotations. Dropped-analysis indicator and latency counters visible during a deliberately slow fake detector; playback never displays stale bounding boxes as current.

### R8.3-D — Ground truth quality, scores, dashboard

- Separate **proposal-review correctness**, **per-frame incident recall**, and **event/interval-level detection**. Compute TP/FP/FN against *independently labeled* truth only on a clearly declared fully reviewed subset. Unlabeled video gaps are unknown, not TN.
- Recall \`TP/(TP+FN)\`, precision \`TP/(TP+FP)\`, false-positive count per minute and incident miss rate by 2D/3D/photo; include denominators, data counts, and zero-denominator \`N/A\` (not 0% or 100%).
- Confusion-matrix and threshold sweeps split by model ID/hash and source type; not global mix of NudeNet/CLIP scores.
- Separate train/val/test by source, creator/character/series and near-duplicate media so video frames or crop variants from one source never appear across splits. Freeze held-out evaluation examples. Avoid treating a skewed X search feed as representative real-world prevalence.
- Acceptance: synthetic fixtures with mathematically expected TP/FP/FN and multiple intervals; ensure empty/uncertain/unscanned video intervals don't inflate accuracy; versioned report JSON with no source pixels.


### R8.3-E0 — Browser-first X source discovery (DEFAULT, no paid X API)

**Design decision 2026-11-11:** Prefer embedded WebView2 or external browser fallback for manual X searches. This replaces paid X API use for the **default** X workflow; official API integration below remains a distinct future option.

- WPF X browser tab opens https://x.com with normal manual search and X native image/video tabs. Toolbar: search input, back, forward, reload, open in external browser, and URL bookmark. Permit user-driven username/keyword search. No app-held X API credentials.
- Reviewer explicitly pastes a post URL into a local reference queue or clicks a Save current permalink button when the visible URL is a real post permalink. Save only the post permalink and local reviewer notes/tags. No extraction of X page DOM, image pixels, video streams, thumbnails, cookies, network traffic, or user datasets.
- A local image/video already owned or explicitly licensed for evaluation can be imported separately with the normal file picker, with provenance reference linked only by user action. **Embedded X video playback is not automatically frame-by-frame analyzable**. Browser viewing does not equal license to download, train or redistribute.
- Never use Playwright/Selenium extraction, automatic scrolling, injected page scripts for content capture, private endpoints, scraping, cookie extraction or bypass of access controls. X prohibits unapproved scraping and non-API website automation. Respect user privacy and deletions.
- Cost banner: "X API利用料金0円（通常ブラウザ閲覧のみ）". This is an X API fee statement, not a claim about ISP fees, subscriptions or every other cost.
- Browser profile and URLs: no host objects for X, HTTPS-only, block unsafe schemes, log no auth cookies/session identifiers, provide external-browser fallback for authentication, playback and X-side restrictions.
- Acceptance: manual X search by term/user, navigation to native image/video tabs, explicit saving/opening of a post permalink, no X API requests, local-only reference store, WebView2 Windows desktop smoke. Mock URL-validation tests must reject arbitrary protocols and false X permalink formats.
- **Deferred enhancement**: A separately approved and billable official X API gallery is optional, not a blocker for R8.3.

### R8.3-E — Optional PAID X API source discovery (DEFERRED, after local lab)

**Feasibility from official X docs checked 2026-11-10:**
- Recent search \`GET /2/tweets/search/recent\` searches last 7 days, with \`has:images\`, \`has:videos\`, \`from:handle\` and \`-is:retweet\` operators, pagination. Historical archive search \`/2/tweets/search/all\` requires pay-per-use/Enterprise access.
- Author posts \`GET /2/users/:id/tweets\` for an authorized public profile; filter media type after results as necessary. The user profile timeline is limited by endpoint retention, not a promise to enumerate all past media.
- Query images/videos using supported media expansions and media fields when present. The UI must gracefully handle missing thumbnails, restricted or non-playable media, deleted and withheld posts.
- Display a grid, filters (user/keyword, images/videos, newest, reviewed/unreviewed), post author/link/source attribution, one-click "Open on X"; allow explicit "Add as reference" when permitted.
- **Do not assume video raw bytes are available from API or embed playback.** Embedded X players can be viewed, but WPF cannot automatically infer every video frame from an authenticated embedded player. For actual pixel-by-pixel model inference, require explicitly authorized local media or a supported lawful ingestion path. Never bypass access controls, DRM, authentication, rate limits or post download restrictions.
- Authentication through official app/API permissions; no password harvesting. Protect app token using Windows platform credential vault/DPAPI, NEVER GitHub commits, project JSON or telemetry.
- **Cost guard:** official pricing page (2026-11-10) indicates $0.005/post read, $0.010/user read, special owned-read prices where applicable; displayed estimates are non-binding and can change. Explicit user-configurable cap: max posts/page=20 default, max pages/session=3, total billable budget and per-search confirmation. Query only on explicit search click; honor 429/backoff and retries; no automatic infinite scroll polling.
- Compliance: X's Developer Policy on display/redistribution/deletions, protected accounts, privacy and consent applies. Keep public X content discovery separate from "cleared for evaluation/training": public visibility is NOT permission for redistributing, caching indefinitely, or training on sexual images. Retain at most permitted identifiers + expiring local metadata; respect deletions/removals and rights requests.
- Acceptance: all X endpoint tests use mocked API JSON (zero paid queries in public CI); auth denial, 429, API outage, deleted post and media missing cases; no unexpected charges, no raw private pixels in logs or exported reports.

**Official X references**
- https://docs.x.com/x-api/posts/search/introduction
- https://docs.x.com/x-api/posts/search/integrate/operators
- https://docs.x.com/x-api/posts/timelines/introduction
- https://docs.x.com/x-api/getting-started/pricing
- https://docs.x.com/developer-terms/policy

### R8.3-F — Evidence and Windows UX regression

- Add end-to-end synthetic tests for review queue, PTS timeline, fault/slow inference, self-contained EXE builds, and offline source integrity.
- Verify WPF media playback, annotation controls, and logging on actual Windows desktop; CI compiling and unit tests are not proof of an interactive UI working.
- Research models stay opt-in offline; no livestream OBS release feature claimed. An annotation assistant is not a reliable safety mechanism.

## Implementation order / handoff

| Phase | Dependency | Target output | Exit condition |
|---|---|---|---|
| A | R8.2 merged | Local annotation schema/storage, no UI | Synthetic schema/undo/hash tests pass |
| B | A | Image review queue with keyboard labeling | Review/restore 20 synthetic images without losing decisions |
| C | A + existing R7 video | Video playback annotations, PTS overlay, rescan timeline | 133ms pulse missed by lower sampling visibly logged; local video review works |
| D | A–C | Annotated evaluation and report | Correct synthetic TP/FP/FN and zero-denominator rules |
| E0 | A/B, browser only | Manual WebView2 X native search and permalink bookmarking | Human UI smoke + no X API usage + local metadata only |
| E (deferred) | Optional official X API | Structured X gallery | Only if separately approved, mock cost/security tests pass |
| F | A–D + E0 | Real Windows manual QA + handoff | Human verified playback, X manual browsing and project persistence; no unsupported safety claims |

**Priority if time limited:** A → B → C → D. X (E) should not block creating ground truth from local rights-cleared assets. X discovery defaults to E0 manual WebView2 browser and the paid API path E is deferred unless explicitly approved.

## Out of scope

- Browser cookie extraction, scraping behind login, mass media downloading, bypassing paid API/rate limits, protected or age-gated account access.
- Nonconsensual sexual-content data gathering, underage imagery, inference on unknown-rights content as a training corpus.
- Automatically deeming unscanned/ambiguous content safe, guaranteeing live OBS exposure prevention, or asserting model accuracy without independent annotations.
