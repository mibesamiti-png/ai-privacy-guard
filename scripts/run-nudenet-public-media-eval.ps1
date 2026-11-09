$ErrorActionPreference = 'Stop'
$target = Join-Path $env:RUNNER_TEMP 'apg-nudenet-publicmedia-model.onnx'
$destination = 'dist/nudenet-public-media-eval'
$url = 'https://huggingface.co/deepghs/nudenet_onnx/resolve/44956a0587943016350c7815f9e0dec089d45cae/320n.onnx'
$expected = 'C15D8273ADAD2D0A92F014CC69AB2D6C311A06777A55545F2C4EB46F51911F0F'
try {
    python -m pip install --disable-pip-version-check --quiet Pillow==11.3.0
    if ($LASTEXITCODE -ne 0) { throw 'Pillow installation failed' }
    python scripts/make_nudenet_public_media_eval.py --output $destination
    if ($LASTEXITCODE -ne 0) { throw 'Public-media source images failed coverage requirements.' }
    Invoke-WebRequest -Uri $url -OutFile $target -MaximumRedirection 10 -TimeoutSec 180
    if ((Get-FileHash -Path $target -Algorithm SHA256).Hash -ne $expected) {
        throw 'NudeNet pinned model integrity check failed'
    }
    dotnet run --no-build --no-restore -c Release --project tests/PrivacyGuard.AI.Smoke/PrivacyGuard.AI.Smoke.csproj -- $target --visual-fixtures $destination "$destination/report"
    if ($LASTEXITCODE -ne 0) { throw "Real media ONNX evaluation failed with code $LASTEXITCODE" }
    python scripts/make_nudenet_eval_maps.py --root $destination
    if ($LASTEXITCODE -ne 0) { throw 'Box-only map generation failed' }
    Write-Host 'NUDENET_REALMEDIA_EVAL_PASS: source images evaluated, metadata and abstract coordinate diagrams generated.'
}
finally {
    # Do not redistribute copyright imagery or identifiable third-party people.
    # Only source links, pixel dimensions, hashes, class scores, bounding boxes,
    # timings, and blank-background coordinate maps can leave this CI job.
    if (Test-Path "$destination/inputs") {
        Remove-Item "$destination/inputs" -Recurse -Force
    }
    if (Test-Path "$destination/report") {
        Get-ChildItem "$destination/report" -Filter "*.bmp" | Remove-Item -Force
    }
    Remove-Item $target -ErrorAction SilentlyContinue
}
