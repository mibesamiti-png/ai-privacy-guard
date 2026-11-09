# R1.1 implementation delta

Date: 2026-11-09

## Added

1. `PrivacyGuard.Core/RegionEditor.cs`: normalized rect move / four-corner resize / reversed-drag creation, clamped to image bounds.
2. `PrivacyGuard.Core/PreviewProcessor.cs`: memory-bounded thumbnail creation and downscaled comparison preview, with no modification to the source pixels.
3. ReviewItem now exposes `Thumbnail` and `RegionText` with `INotifyPropertyChanged` updates.
4. WPF left/center/right comparison: editable original image, reduced-resolution processed preview and full-resolution selected crop/zoom.
5. Review queue uses image thumbnails and virtualized ListBox items.
6. Mouse move/resize controls and a current-session region undo stack.
7. SQLite `RegionEdits` audit table; `UpdateBounds` commits geometry and audit in one transaction.
8. Core xUnit tests for geometry/clamping and preview samples; synthetic, non-person image fixture.

## Not in this round

No real AI model, video decoder, folder processing, OBS, live safety, or Windows build verification.

## Suggested verification order

1. Run .NET 10 restore/build/test on Windows.
2. Load `samples/synthetic_demo.png` and test queue crop, drag move, all four handles, undo and restart persistence.
3. Verify original/full PNG masks and compare preview differences.
4. Use real production-quality image samples only with consent; do not commit them to a public repository.
