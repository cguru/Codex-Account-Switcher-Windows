[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$vendorRoot = Join-Path $projectRoot "vendor"
$targetExe = Join-Path $vendorRoot "codex-auth.exe"
$targetLicense = Join-Path $vendorRoot "LICENSE-codex-auth.txt"

New-Item -ItemType Directory -Force -Path $vendorRoot | Out-Null
if ((Test-Path -LiteralPath $targetExe) -and (Test-Path -LiteralPath $targetLicense)) {
    Write-Host "codex-auth vendor payload is ready."
    return
}

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required to prepare the codex-auth installer payload. Install Node.js 22 or newer."
}

$packageRoot = $null
$globalRoot = (& npm root -g).Trim()
$globalCandidate = Join-Path $globalRoot "@loongphy\codex-auth"
if (Test-Path -LiteralPath $globalCandidate) {
    $packageRoot = $globalCandidate
}
else {
    $cacheRoot = Join-Path $projectRoot ".vendor-cache"
    & npm install --prefix $cacheRoot --no-save "@loongphy/codex-auth@0.2.10"
    if ($LASTEXITCODE -ne 0) { throw "Failed to download codex-auth 0.2.10." }
    $packageRoot = Join-Path $cacheRoot "node_modules\@loongphy\codex-auth"
}

$sourceExeCandidates = @(
    (Join-Path $packageRoot "node_modules\@loongphy\codex-auth-win32-x64\bin\codex-auth.exe"),
    (Join-Path (Split-Path -Parent $packageRoot) "codex-auth-win32-x64\bin\codex-auth.exe")
)
$sourceExe = $sourceExeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$sourceLicense = Join-Path $packageRoot "LICENSE"
if (-not $sourceExe) {
    throw "Missing codex-auth payload. Checked: $($sourceExeCandidates -join ', ')"
}
foreach ($required in @($sourceExe, $sourceLicense)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing codex-auth payload: $required" }
}

Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath $sourceLicense -Destination $targetLicense -Force
Write-Host "Prepared codex-auth 0.2.10 vendor payload."
