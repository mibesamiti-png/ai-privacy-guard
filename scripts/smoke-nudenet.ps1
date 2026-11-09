param(
    [string]$ModelPath = (Join-Path $env:TEMP 'ai-privacy-guard-nudenet-320n.onnx')
)
$ErrorActionPreference = 'Stop'
# Third-party model is downloaded only to the ephemeral runner, NEVER packed
# in GitHub artifacts or the repository. A pinned SHA ensures integrity.
$url = 'https://huggingface.co/deepghs/nudenet_onnx/resolve/44956a0587943016350c7815f9e0dec089d45cae/320n.onnx'
$expected = 'C15D8273ADAD2D0A92F014CC69AB2D6C311A06777A55545F2C4EB46F51911F0F'
Write-Host 'Downloading an external NudeNet ONNX model to TEMP for synthetic inference test.'
New-Item -ItemType Directory -Force -Path (Split-Path $ModelPath -Parent) | Out-Null
Invoke-WebRequest -Uri $url -OutFile $ModelPath -MaximumRedirection 10 -TimeoutSec 180
$actual = (Get-FileHash -Algorithm SHA256 -Path $ModelPath).Hash
if ($actual -ne $expected) {
    Remove-Item $ModelPath -ErrorAction SilentlyContinue
    throw "External model hash mismatch; refusing to run or install."
}
try {
    dotnet run --no-restore --no-build -c Release --project tests/PrivacyGuard.AI.Smoke/PrivacyGuard.AI.Smoke.csproj -- $ModelPath
    if ($LASTEXITCODE -ne 0) { throw "ONNX smoke test failed with code $LASTEXITCODE" }
} finally {
    Remove-Item $ModelPath -ErrorAction SilentlyContinue
}
