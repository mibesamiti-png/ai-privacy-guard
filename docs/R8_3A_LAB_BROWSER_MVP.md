# R8.3-A + E0 — Local Validation Lab & browser-first X discovery (MVP)

## Delivery scope (2026-11-11)

Code introduced in this branch:
- **Core**: \`ValidationLabStore\`, a local-only versioned JSON project with independent *ground-truth* status and review state (not the existing masking approval), SHA-256 media identity, manual rights confirmation, manually added region annotations with microsecond video intervals, bookmark-only X post references, persistent Undo and a previous-version backup.
- **WPF**: new experimental "検証ラボ" tab. Import consented/rights-cleared image/video, inspect the source via existing image review tab or a local video player, confirm **whole-image negative** (never whole-video negative), mark uncertain, undo, view saved media and X post URLs.
- **X**: a manual Microsoft WebView2 browser (SDK version 1.0.4314.35) using normal X web pages, normal X keyword/user search, native X photos/videos browsing, back/forward/reload, external-browser fallback and post permalink save. **No X REST API calls, no hidden API access, no page extraction, no media download**.
- **Tests**: independent Core xUnit tests for content identity, revision persistence, negative vs unreviewed/uncertain, positive regions/times, undo, URI canonicalization, unsupported schemes and source rights.
- **New project store**: \`%LOCALAPPDATA%/AIPrivacyGuard/validation_lab.json\` with \`.bak\` last-snapshot backup. The private file may contain local source paths and X post references and **must never be uploaded to a public repository**. X login browser data is held separately under \`%LOCALAPPDATA%/AIPrivacyGuard/x-webview2-profile\`. The app does not read or export X login cookies.

## What the new UI really does

- Browses X visually; users use X's own Photos/Videos tabs and standard web search. User clicks "表示中の投稿URLを登録" or pastes a post permalink to save its canonical URL only. There is **not yet an independent X image grid**; the displayed gallery is X's own UI.
- Links are references, **not licenses**: a public post must not automatically be downloaded, processed, distributed, or used to train AI. Processing runs only on separately imported locally approved assets.
- "目視確認：対象なし" is enabled by an explicit confirmation dialog for **image files only**, only if content SHA-256 still matches. Video cannot become negative in one click, and an unreviewed source cannot be counted as a true negative. '判断保留' is distinct.
- The Core can hold manually positive region bounds/timestamps but this MVP **does not yet expose rectangle-annotation/video timeline UI**; those are phase B/C. Registering a source does not claim model precision/recall.
- The existing WPF masking detector queue and video offline scan are unchanged, and experimental R8.1/R8.2 pretrained research models are **not wired into the browser**.
- This is a convenience tool, **not** a certified accidental-exposure detector or safe OBS livestream protection.

## Cost, browser and privacy

- Web browsing uses \`https://x.com\`, not paid \`api.x.com\` requests made by the application. **X API fees for this application workflow are $0**, not necessarily internet access/subscription costs. X website itself may make requests internally as a normal site.
- WebView2 SDK is bundled with the app at build time; the **Microsoft Edge WebView2 Runtime** is still required on Windows. If runtime or X login/playback is unsupported, the "外部ブラウザで開く" button works as fallback.
- Users interact manually with X: no Selenium/Playwright scrolling, DOM scraping, browser script injection, cookie extraction, network interception, private API endpoints, bulk downloading or rate-limit bypass.
- WebView2 uses a dedicated local browser profile; leaving X logged in persists session cookies managed by the browser runtime. UI doesn't access those cookies. Windows users can sign out from X and/or remove the app's browser profile when they wish.
- Source hash is checked when reviewer opens or changes label; if the source moved or changed, source labels are not reused. **Unit CI and compilation do not prove live X behavior**, website access or WPF graphics render.

## Verification / acceptance

1. **Build and xUnit** on Windows GitHub Actions, including old Core/OBS/API tests, WebView2 dependency restore and WPF XAML compilation.
2. Synthetic/local Core tests for duplicate unchanged image, modified same path, full image negative, uncertain, partial positives with video timestamps, undo across restart, backup and corrupt project.
3. Browser acceptance **needs a local Windows interactive test**: WebView2 installed, x.com logged in or public browsing, search, Photos/Videos native tabs, post permalink saved, app relaunch shows bookmark. X website/network failures are not assumed to be app failures.
4. Confirm no user source media, X passwords/cookies, or review JSON are committed to repo or public CI.

## Remaining R8.3 order

- **B**: image thumbnail gallery with shortcuts, frame/region-level positive/negative/uncertain annotation, manual missed-box edit and autosave.
- **C**: true video playback under WPF with PTS-synchronized candidate overlays, step/repeat, live slow-model latency/coverage vs separate offline R7 prescan, manual missed interval annotation.
- **D**: held-out per-domain (2D/3D/photo) metrics and false-negative accounting on fully labeled subsets, avoiding artificial model score calibration.
- **E optional**: custom X API gallery only if user explicitly authorizes future billing. Default remains no API.

Source-of-truth plan: [R8.3 validation lab roadmap](R8_3_VALIDATION_LAB_ROADMAP.md).
