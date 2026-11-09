# R4.2 video review content identity

The R3–R4.1 `VideoReviewProject` used source path, length and mtime only. A changed file can retain the same length and last-write time. It was not safe to carry forward approvals automatically.

Schema version 2 adds `SourceSha256`. `VideoReviewStore.Load` verifies current SHA-256 against the saved value. The batch export computes an independent source content hash and passes it to `Load`; the export also validates source content again before publishing. A mismatch raises an exception, blocking export.

## Backwards compatibility
A version-1 review file matching path, size and mtime may be loaded as *candidate suggestions only*. Every decision is changed to `Pending` in memory, without deleting the underlying legacy file. The user must individually review and re-approve those regions. The next save writes version 2 with a SHA-256. Old completed export receipts must not bypass this requirement.

## Important distinction
On regular `Load` the hash is calculated from the current file. `Save` can reuse the cached hash from the open session to avoid re-hashing large videos on every UI edit, so a concurrent same-stat file replacement could make saved metadata stale. Batch export independently recomputes hash and rejects this case. The system is not tamper-proof against concurrent hostile filesystem manipulation.

Do not upload user's source media, crops, or review JSON into GitHub or public CI. Synthetic fixtures only.
