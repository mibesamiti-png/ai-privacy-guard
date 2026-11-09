$ErrorActionPreference = 'Stop'
$target = Join-Path $env:RUNNER_TEMP 'apg-nudenet-visual-model.onnx'
$destination = 'dist/nudenet-visual-eval'
$url = 'https://huggingface.co/deepghs/nudenet_onnx/resolve/44956a0587943016350c7815f9e0dec089d45cae/320n.onnx'
$expected = 'C15D8273ADAD2D0A92F014CC69AB2D6C311A06777A55545F2C4EB46F51911F0F'
try {
    python -m pip install --disable-pip-version-check --quiet Pillow==11.3.0
    if ($LASTEXITCODE -ne 0) { throw 'Pillow unavailable; fixture generator cannot run.' }
    python scripts/make_nudenet_eval_fixtures.py --output $destination
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }
    Invoke-WebRequest -Uri $url -OutFile $target -MaximumRedirection 10 -TimeoutSec 180
    $hash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
    if ($hash -ne $expected) { throw 'NudeNet model hash mismatch; inference aborted.' }
    dotnet run --project tests/PrivacyGuard.AI.Smoke/PrivacyGuard.AI.Smoke.csproj --configuration Release --no-build --no-restore -- $target --visual-fixtures $destination "$destination/report"
    if ($LASTEXITCODE -ne 0) { throw "Actual ONNX fixture inference returned $LASTEXITCODE" }
    # Only ship the viewable fixtures, annotated/masked preview and report.
    Get-ChildItem -Path "$destination/inputs" -Filter '*.bgra' | Remove-Item -Force
    Write-Host 'NUDENET_VISUAL_EVAL_PASS: artifacts generated from a real pinned model.'
} finally {
    Remove-Item $target -ErrorAction SilentlyContinue
}
