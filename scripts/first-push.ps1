# Push the source folder to the user's OWN newly created, empty private GitHub repository.
# Do not embed GitHub tokens in this script. Uses Git Credential Manager if needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$remote = 'https://github.com/mibesamiti-png/ai-privacy-guard.git'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required: https://git-scm.com/download/win'
}

Write-Host 'TARGET: ' $remote
Write-Host 'Create the EMPTY PRIVATE repository at https://github.com/new first.'
Write-Host 'Do NOT initialize the repository with README/.gitignore/license.'
$ok = Read-Host 'Type YES to initialize and push this source to the private repository'
if ($ok -cne 'YES') { Write-Host 'Cancelled; no GitHub data was sent.'; exit 0 }

if (-not (Test-Path .git)) {
    git init
    if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
}
git branch -M main
if ($LASTEXITCODE -ne 0) { throw 'git branch failed' }

$existing = git remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($existing.Trim() -ne $remote) {
        throw "An existing origin points to $existing; refusing to replace it."
    }
} else {
    git remote add origin $remote
    if ($LASTEXITCODE -ne 0) { throw 'git remote add failed' }
}

git add -A
if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
Write-Host 'FILES STAGED FOR COMMIT:'
git diff --cached --name-only
$send = Read-Host 'Review file names above. Type PUSH to continue'
if ($send -cne 'PUSH') { Write-Host 'Stopped before commit/push.'; exit 0 }

git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -m 'Initial import: AI Privacy Guard R4.2 and Windows CI'
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed; configure git user.name/email if needed' }
}
git push -u origin main
if ($LASTEXITCODE -ne 0) { throw 'git push failed; verify the repository exists and GitHub login is active' }
Write-Host 'Push complete. Check: https://github.com/mibesamiti-png/ai-privacy-guard/actions'
