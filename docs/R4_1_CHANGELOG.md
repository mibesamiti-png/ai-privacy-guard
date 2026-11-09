# R4.1 changes — 2026-11-09

**New:** C# Core `BatchExportPlanner`/receipt model, `BatchExportStore` atomic snapshots, image review identity lookup, WPF batch export list, output folder selection, approved-only output, image PNG and video MP4 batch exports, cancellation, resume with SHA-256 verification, collision-safe filenames, and staging-to-final publish.

**Policy:** Do not export a media file with pending/deferred candidates or without a user-approved mask. Existing scans with only manual reviews can be exported. All filenames differ from inputs, and exports are never overwritten.

**Regression surface:** `ImageCodec.WritePng` and `FfmpegVideoService.ExportBlackMasksAsync` gained optional `overwrite` flags with existing single-file behavior retained; R4.1 uses `overwrite:false` for staged outputs. C# xUnit tests cover naming/validation/fingerprint/reuse/approved-only pixel mask. A new Python/FFmpeg test verifies reviewed time-range black box, retained audio and unchanged input.

**Validation:** Python unittest suite **18/18 pass**; static XAML/reference/handler check passes. The added C# tests **are authored but not run**. .NET SDK / Windows runtime not present. No validated exposure model bundled. No remote GitHub/Notion changes.
