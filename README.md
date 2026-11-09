# AI Privacy Guard — R7 Windows prototype

Privacy-conscious offline image, video and experimental live-OBS content review application for Windows, built in **C#/.NET 10 / WPF**.

**Project state (2026-11-10):** R7 code merged via [PR #14](https://github.com/mibesamiti-png/ai-privacy-guard/pull/14). Windows PR CI [#42710234669](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42710234669) passed (121/121 Core xUnit, 7/7 OBS xUnit, 21 Python/FFmpeg tests, executable publication). Interactive Windows WPF UI and genuine privacy-exposure detection performance are **not** verified. **Do not treat this tool as a reliable live privacy protection barrier.**

**R8.0 architecture update (2026-11-10):** Independent full-frame multi-model proposal analysis was merged through [PR #16](https://github.com/mibesamiti-png/ai-privacy-guard/pull/16) and passed [Windows CI #42715218212](https://github.com/mibesamiti-png/ai-privacy-guard/actions/runs/42715218212) (129 Core, 7 OBS, 21 Python tests). This is an *offline Core API only*: no new trained garment-specialist model, no WPF/OBS integration, no verified detection recall. The Windows app remains at R7 user-facing capability. See [R8 design](docs/R8_MULTIMODEL_PROPOSALS.md).

## Supported functions

- Open image; manually mark, review and apply black/blur/pixel masks.
- Optional **third-party NudeNet 320n** ONNX model download to the user's LocalAppData with pinned SHA-256; local inference for candidates, not guaranteed detection. No external images are uploaded for inference.
- Video frame seek, approximate timestamp index, candidate review and mask keyframes.
- **R7 offline video sample scan:** 1, 2, 4, 8 and 12 samples/sec (default 2); up to 30,000 samples per scan, with approximate gaps/low-source-fps warnings. Pending approval still required before export.
- Local batch analysis, review-before-export, source/hash-bound output comparison.
- Experimental separate localhost OBS Browser Source bridge, watchdog black output, camera reconnect, optional manual audio offset. No reliable live crash recovery or validated detection recall.

## Download and run

1. Open [GitHub Actions — Windows builds](https://github.com/mibesamiti-png/ai-privacy-guard/actions/workflows/windows-build.yml) and choose the latest **successful `main`** build.
2. Download the `AIPrivacyGuard-R7-Windows-x64` artifact (or earlier version-named archive for older successful runs). Fully extract the directory.
3. Run `AIPrivacyGuard.exe`. This build is self-contained; Windows x64 is required. FFmpeg/ffprobe may be required on PATH for full video features.
4. To use NudeNet, select the explicit **露出検出AIを導入（NudeNet）** button and read its license/precision warning. No model is downloaded automatically without user consent.
5. Use the video tab's full-video scan rate selector for 8/12fps when a higher offline sampling density is needed; scan cost and false positives can increase.

GitHub Actions artifacts are temporary and may require GitHub sign-in. No third-party ONNX weights or user images are bundled in this public repository.

## Safety and accuracy boundaries

- NudeNet has no dedicated label for a skirt exposing underwear. It may miss brief incidents, small details, dark images and 2D/3D characters.
- Even 12fps offline sampling leaves ~83ms nominal gaps and cannot detect events between sampled frames. VFR rounding and imperfect models add more uncertainty.
- A 12fps 30,000-frame scan limit makes long videos require a lower rate or a future segment-based design.
- All automatically generated candidates remain **Pending** until a user reviews them. Approving a mask on sampled frames does not establish coverage of unsampled frames.
- OBS Browser Source and camera hardware have not been fully tested on a real streaming setup. An OBS renderer failure may freeze a previously visible frame.

## Development and handoff

- Latest project handoff: [docs/HANDOFF.md](docs/HANDOFF.md)
- Current feature status: [docs/FEATURE_STATUS.md](docs/FEATURE_STATUS.md)
- R7 dense sampling design/test: [docs/R7_HIGH_DENSITY_VIDEO_SCAN.md](docs/R7_HIGH_DENSITY_VIDEO_SCAN.md)
- R6 model provenance and known limitations: [docs/R6_NUDENET_MODEL.md](docs/R6_NUDENET_MODEL.md)
- Test and CI history: [docs/TEST_RESULTS.md](docs/TEST_RESULTS.md)
- Windows build/source integrity: `scripts/build.ps1`, `scripts/check_source.py`, `.github/workflows/windows-build.yml`

To build locally, install .NET 10 SDK and compatible FFmpeg tools, then run `dotnet build AIPrivacyGuard.slnx -c Release` followed by `dotnet test tests/PrivacyGuard.Core.Tests/PrivacyGuard.Core.Tests.csproj -c Release`.

Public repository: do not commit private media, identifiable user images, personal review databases, local passwords, or unlicensed model files.
