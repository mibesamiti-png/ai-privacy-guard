# R4.2 before/after comparison contract

1. Choose a row in the completed-export list. Rows not marked Completed cannot be compared.
2. Enter the target timestamp in seconds for videos; the time field is ignored for images. The input is bounded to nonnegative finite seconds and clamped within the shorter video duration.
3. Click **選択した出力を比較** in the batch tab to switch to the dedicated **出力比較** tab; click the button with the same label inside that tab to load the preview. The UI recalculates original SHA-256 and output SHA-256, validates source signature, retrieves current reviews and checks their fingerprint against the receipt. It rejects changed/stale results.
4. Preview frames appear side by side, in memory only. JPEG/PNG images are downscaled to bounded previews; videos use FFmpeg to extract one frame per input at the same approximate target timestamp.
5. The **表示サンプルの確認を記録** button only enables after successful comparison. It revalidates source/reviews/output before writing an audit timestamp and video sample timestamp to the receipt.
6. Changing row or regenerating the preview clears previous transient proof. Receipt metadata is not a guarantee of content safety.

Video preview is based on approximate time seeking; VFR frame boundaries, encoder timing and stream start offsets can differ. It is NOT exact frame-to-frame comparison, does not show all frames, and does not detect unseen exposures. Full-video human review is still recommended before publishing.
