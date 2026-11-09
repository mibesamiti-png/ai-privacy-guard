# AI Privacy Guard — GitHub bootstrap (R4.2 source baseline)

**GitHub setup:** see [`docs/GITHUB_SETUP.md`](docs/GITHUB_SETUP.md). GitHub repository and Windows CI have **not** been created/run by this ZIP.

---

# AI Privacy Guard — R4.2 source prototype

**Status: R4.2 source code authored; Windows/.NET C# build, WPF runtime, xUnit and real ONNX exposure inference are NOT verified. This is not suitable for live broadcasting.**

## What's new since R4.1

- **Before/after output viewer:** choose a completed batch export and compare the original versus processed image side-by-side. Videos show source and output frames from the same approximate timestamp supplied in seconds.
- **Separate sample audit:** after visually inspecting the displayed frames, record the time inspected. This DOES NOT certify entire video safety.
- **SHA-256 video review binding:** video review schema v2 binds decisions to source content. Legacy review schema 1 loads all decisions as `Pending`; reapproval is required before export.
- **More consistency checks:** batch comparison verifies the current source hash, review fingerprint, and output hash before showing a preview, and rechecks before saving an audit marker.
- **Synthetic tests:** Python/FFmpeg test suite now contains 20 tests; extra C# xUnit tests are written but not executed without the Windows .NET 10 SDK.

## Windows verification steps

1. Install .NET 10 SDK and `ffmpeg`/`ffprobe` on PATH. Run `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1` from the project directory.
2. Launch the published WPF app, open `samples\batch_fixture` and verify synthetic image/video preview and manual review editing. The dedicated exposure detection model is NOT included.
3. Approve a mask for an image or video, export to the default output folder and select the completed export row.
4. Click **選択した出力を比較** to switch to the dedicated **出力比較** tab, then click the same label inside that tab to render original / output. For video, enter a timestamp (such as `0.5`) and click again for that sample. Click **表示サンプルの確認を記録** only after actually checking the displayed sample.
5. Restart and check persistence. Validate old schema-v1 video reviews require renewed approvals. Do not rely on this tool as an automatic safety barrier.

## Known limitations

- No validated exposure detection model; compatible ONNX model + manifest are not bundled. Generic person detection is not exposure detection.
- R4 batch video scanning is sparse (1fps), and dynamic masks are keyframe-interpolated; missing exposure events and tracking errors are possible.
- Video comparison is an approximately seeked *single frame*, not exhaustive or frame-perfect. Output audio is not previewed side by side.
- Source SHA-256 can be slow for large videos. Windows GUI compile/runtime need actual Windows validation.
- Users' original media, crops, and private review records are not in the source ZIP or uploaded anywhere.

See `docs/R4_2_CHANGELOG.md`, `docs/HANDOFF.md`, `docs/VIDEO_REVIEW_INTEGRITY.md`, `docs/BATCH_COMPARISON.md`, `docs/TEST_RESULTS.md`.
