# AI Privacy Guard R4.2 handoff

## Current snapshot
`AIPrivacyGuard_R4_2_Source.zip` / folder `AIPrivacyGuard_R4_2` contains all R4.1 sources plus R4.2 comparison and video integrity extensions. No user media, crops or personal review project are included. No GitHub or Notion modification in this turn.

## New code
- `src/PrivacyGuard.App/BatchExport.xaml.cs`: comparison pane event handlers, hash + review check, sampled frame output, sample audit.
- `src/PrivacyGuard.App/MainWindow.xaml`: before/after images and video timestamp control.
- `src/PrivacyGuard.App/Services/VideoReviewStore.cs`: schema v2 with source SHA-256 and downgrade of v1 approvals to Pending.
- `src/PrivacyGuard.Core/VideoReviewBinding.cs`: pure hash/version binding policy.
- `src/PrivacyGuard.Core/BatchExport.cs`: receipt sample audit fields.
- C# xUnit: `VideoReviewBindingTests.cs` (not run).
- Python harness: `test_r42_integrity_preview.py` (passed), full Python suite 20 passed.

## Critical next steps, in order
1. On Windows with .NET 10 SDK run `scripts/build.ps1` and fix real compiler/XAML errors; run xUnit and launch app. Check GitHub Actions workflow only after explicitly adding this source to an approved repository.
2. Windows smoke: load synthetic image and video, approve manual masks, batch export, select receipt and compare original/output at 0.1 and 0.5 seconds, mark sampled comparison, restart and check receipt persisted.
3. Legacy migration smoke: use an old schema-v1 synthetic review project; verify old Approved decisions become Pending and cannot export until reapproval. Tamper video with same length/mtime and ensure v2 load/export blocks stale review.
4. Test large video hashing UX, cancellation, memory use, odd dimensions and VFR output offset behaviour.
5. Implement proper full-video review scrub and reviewer checklist. DO NOT progress to live fail-closed OBS until full Windows validation is complete.

## Safety caveats
C# compile and WPF UI still unverified, specialized exposure model not bundled, sparsely sampled scanning may miss frames. Visual sample audit is not a safety certificate. No media uploaded externally.
