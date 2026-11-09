# R4.0 — Folder inventory and resumable batch analysis

## Added

- `PrivacyGuard.Core/BatchQueue.cs`: deterministic file inventory, supported image/video filters, recursive folder option, output-folder exclusion, 10,000-file bound, file-size/mtime signatures, interruption recovery.
- `PrivacyGuard.App/Services/BatchQueueStore.cs`: per-folder atomic local JSON queue snapshots in `%LOCALAPPDATA%\AIPrivacyGuard\batch_queues`. Never saves pixels/crops/video frames.
- `PrivacyGuard.App/BatchReview.xaml.cs`: batch UI handlers, progress, cancel/resume, retry errors, reset all for a new model, open selected item in existing image/video review UI.
- Image AI batch: read, infer, hash, file-change check, deduplicate, save **Pending** detection records into existing SQLite review store. No auto approval.
- Video AI batch: FFmpeg/FFprobe probe and **1fps sampled** frame inference, temporal event grouping, append **Pending** events into existing video review JSON. No auto masking/export.
- `BatchQueueTests.cs`: C# unit tests authored for recursive enumeration, output exclusion, changed-file signatures, interruption/changed-file reconciliation, path prefix.
- R4.0 tab and source-path loader reuse; prior R3.3 image and video workflows remain accessible.
- Optional `samples/batch_fixture` non-person assets for manual Windows acceptance checks.

## Guarantees / limits

- **No explicit AI model -> inventory only.** Nothing is silently labeled safe.
- **No automatic output, original file overwrites or external upload.** Processing writes only the local review database/metadata and batch queue JSON.
- A file marked Completed means that the scan pipeline finished, **not** that the file contains no exposure.
- Model changes do not silently rewrite prior reviews: use **全件再解析** to opt in. Detection origin distinguishes model SHA prefix when available.
- Interrupted Running entries reconcile back to Pending when folder is rescanned; existing review results are retained and deduplicated.
- Completed state reuse uses file length+mtime (not a full cryptographic audit of the file). Rescan/reset is required if content changes without these attributes changing.
- GIF, animated image frames, unsupported extensions and external symlinks are intentionally not included in batch scan.
- 1fps sampling can miss events between frames. No trained/validated exposure model is bundled, no live protection.

## Validation performed in this environment

- `python scripts/check_source.py` static XAML/XML/reference and handler scan: PASS.
- `python -m unittest discover -s tests/python -v`: inherited Python/FFmpeg tests PASS.
- `dotnet` SDK is not installed here: **Windows WPF compile, C# xUnit and UI interaction are unverified**.

## Next

1. Run Windows CI and build, correct C# compile errors before claiming app works.
2. Manually test batch inventory, 10-file queue, interruption/restart, output exclusion and open-for-review with sample assets.
3. Add R4.1 batch export only after explicit human approval and conservative output path checks.
4. Add per-file media summary and model/precision assessment; do not treat zero detections as an all-clear.
