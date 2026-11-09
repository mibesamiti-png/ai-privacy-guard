# Known issues — R4.2

1. Windows WPF build and GUI smoke tests have **never been executed** in this environment (no .NET SDK or Windows GUI). Potential compile errors remain.
2. No dedicated, validated exposure recognition ONNX model bundled; model accuracy, missed events and false positives are not measured.
3. Batch video scan is 1fps, insufficient for short exposure events; no claim of full coverage.
4. Video tracking is block-match suggestion / keyframe interpolation; abrupt motion, scene cuts and occlusion can move masks incorrectly.
5. Sample comparison is approximate FFmpeg seek at a single selected time, not VFR-exact synchronized comparison or full-video screening.
6. Very large video opening can take time for SHA-256 hash; use async file hashing progress and cancellation in a future version.
7. Reparse-point/junction and hostile concurrent filesystem tampering are not fully hardened.
8. Migration from legacy v1 video reviews intentionally resets all decisions to Pending, requiring reapproval.
9. Video's full-frame inspection / visual scrub / output audio comparison remains to be implemented.
10. Live stream output not implemented; DO NOT use this prototype for broadcasting sensitive footage.
