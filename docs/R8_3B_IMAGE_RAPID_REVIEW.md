# R8.3-B — Rapid human image review (first implementation)

**Status:** implemented in PR #24, Windows CI and interactive desktop testing reported separately. This is an evaluation workbench, not an automatic safety classifier.

## How to use (local Windows)

1. Open **検証ラボ（R8.3試験版）**. Confirm you own the image or have permission to evaluate it. Select one or up to 150 image/video files at a time; source SHA-256 and local paths are recorded only in the local project file, not GitHub.
2. Select an image in the lab's source list, press **画像を連続レビュー**, or open the **画像クイック検証（R8.3-B）** tab and choose an item in its thumbnail list.
3. The reviewer sees the original image with colored overlays. **Blue** = previously stored model proposal, **orange** = selected model proposal, **green** = manually confirmed ground truth. A source starts as *unreviewed*, never assumed negative.
4. Optional: load a compatible detector in the existing **画像・確認キュー** tab, then press **AI候補を検出** in rapid review. Its predictions are still *unverified candidate boxes*. Detection scores are uncalibrated; results are stored locally in the existing history SQLite store. The R8.1/R8.2 Hugging Face research models are **not** integrated.
5. Choose one candidate in the list and a **human truth category** (garment visible, garment occluded, possible exposure, other); mark correct, false positive or uncertain. Correct makes a human-confirmed region in the **separate** ground-truth project and does **not** automatically mark the whole image fully reviewed. False positive does **not** mark the whole image negative.
6. If the model completely missed an area, choose the ground-truth category, press **見逃し枠を描く**, then drag over the image. A minimum of 4 pixels per dimension is required. This saves a human-created positive region even when the model proposed nothing.
7. Only after inspecting **every part of the original image**, press **画像全体を確認完了** for an image with human-positive regions, or **対象なし** for an explicitly inspected image with no positive regions. Both require a Yes/No dialog; unexamined areas cannot be inferred safe. Under the project schema, videos can **never** be marked wholly negative by this action.
8. Use next-unfinished and source filters, undo the previous annotation for the current source, and reopen the app to verify persistence. The \`ValidationLabStore\` keeps 25 undo steps per project and a replaceable prior-version \`.bak\` backup.

## Keyboard shortcuts

| Key | In rapid review tab |
|---|---|
| 1 | Confirm selected AI proposal as a human ground-truth region, using selected truth category |
| 2 | Mark selected AI proposal false positive |
| 3 / M | Arm drag-to-mark missed/undetected region |
| 4 | Mark selected AI proposal uncertain |
| 5 | Open full-image negative confirmation dialog |
| N | Next image not marked fully reviewed |
| P | Previous image |
| Ctrl+Z | Undo most recent annotation only if it belongs to currently shown image |

Shortcuts are disabled while editing text or combo controls. An existing model proposal can be reassessed only after undoing its previous human vote; this prevents silently leaving conflicting truth regions.

## Code and test contract

- \`src/PrivacyGuard.Core/ValidationLabProject.cs\`: \`LabProposalAssessment\` stores reviewer verdict separate from mask approval, actual model ID, candidate identity and raw normalized box. Only human-confirmed proposals create truth regions. Old v1 projects without the new assessments array deserialize with an empty list (no migration loss).
- \`src/PrivacyGuard.App/LabImageRapidReview.xaml.cs\`: human-reviewed visual queue, thumbnails capped in memory to 160 items (all file names still shown), SHA-256 verification on each image load/annotation and before model-result persistence, 4-pixel minimum drag region, local cached proposals, undo.
- \`src/PrivacyGuard.App/MainWindow.xaml\`: new independent rapid-image tab; normal manual WebView2 X tab remains unchanged and does not scrape X.
- \`tests/PrivacyGuard.Core.Tests/ValidationLabRapidReviewTests.cs\`: proper positive/negative/uncertain distinction, no false-positive->negative shortcut, restart undo, model provenance and category checks, previous-project backward compatibility.

## Important limits

- **Not verified in a physical Windows desktop session yet:** WPF layout/display, keyboard focus in WebView2 vs quick tab, drag coordinate mapping, model installation/reuse and UX responsiveness; GitHub Actions compile/xUnit is not a substitute.
- The image queue is local-only. It does **not** create thumbnails by scraping social sites, and X post bookmarks are still references only.
- Source content changes, missing media, malformed datasets and failed detector inference are *not* evidence of safe/non-exposure.
- Categories have different meanings: "clothing visible", "clothing occluded", "possible exposure" **must not be pooled as a single validated exposure category**. Precision/recall by actual incident remains unmeasured until R8.3-D independently labels positive and negative examples with controlled splits.
- GIF: first decoded frame only as in existing image decoder. Animated/video inspection will be part of R8.3-C.
- **Video frame-by-frame playback, time labels, review timeline, 2/12 fps comparison and detection during playback are NOT included in R8.3-B**. Next phase R8.3-C will use existing R7 sample planner and original decoder presentation timestamps, explicitly distinguishing scanned vs unscanned frames.
- The privacy guard's live-stream/OBS detection still is not certified for accident prevention.

## Next phase

R8.3-C: first deliver an in-app local video player with accurate source PTS and a synchronized overlay of existing R7 sampled-frame proposals. Then add frame stepping, manual missed intervals, and optional bounded asynchronous detection while playing (with scanned/not-scanned/drop counters and stale-overlay protections). Never promise completeness at 12 fps.

## Verified PR Windows CI (2026-11-11)

- CI https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42861165081 **SUCCESS** on the prior image-review implementation commit `d9d7516b`. Windows x64 .NET 10 WPF build, publish to EXE and OBS bridge publish succeeded. Core xUnit **160/160**, LiveObs **7/7**, Python/FFmpeg tests **40** with 4 expected conditional skips. Existing NudeNet public 19-image × 3 thresholds workflow returned its pass marker. No real exposed-content correctness metrics were established.
- An additional UI navigation fix (avoiding duplicate decode, disabling the queue while AI inference runs) followed that CI. Verify the **final PR head** CI separately before considering the final revision validated.
- Actual human Windows GUI, keyboard and drawing smoke remains **unverified**, regardless of CI status.
