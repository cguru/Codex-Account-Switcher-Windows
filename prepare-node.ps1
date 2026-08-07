[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$nodeVersion = "24.18.1"
$expectedSha256 = "ec56b84a7551893ab2324ebdfdc4ab974a63b4781162600b68a1293cc3e53765"
$projectRoot = $PSScriptRoot
$vendorRoot = Join-Path $projectRoot "vendor"
$cacheRoot = Join-Path $projectRoot ".vendor-cache"
$archiveName = "node-v$nodeVersion-win-x64.zip"
$archivePath = Join-Path $vendorRoot "node-runtime.zip"
$licensePath = Join-Path $vendorRoot "LICENSE-node.txt"

New-Item -ItemType Directory -Force -Path $vendorRoot,$cacheRoot | Out-Null
if ((Test-Path -LiteralPath $archivePath) -and (Test-Path -LiteralPath $licensePath)) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    if ($actual -eq $expectedSha256) {
        Write-Host "Node.js $nodeVersion portable runtime is ready."
        return
    }
}

$downloadPath = Join-Path $cacheRoot $archiveName
$uri = "https://nodejs.org/download/release/v$nodeVersion/$archiveName"
Write-Host "Downloading Node.js $nodeVersion portable runtime…"
Invoke-WebRequest -Uri $uri -OutFile $downloadPath
$downloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $downloadPath).Hash.ToLowerInvariant()
if ($downloadHash -ne $expectedSha256) {
    throw "Node.js archive checksum mismatch. Expected $expectedSha256, got $downloadHash."
}

Copy-Item -LiteralPath $downloadPath -Destination $archivePath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $licenseEntry = $archive.Entries | Where-Object { $_.FullName -match '/LICENSE$' } | Select-Object -First 1
    if (-not $licenseEntry) { throw "Node.js LICENSE file is missing from the official archive." }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($licenseEntry, $licensePath, $true)
}
finally {
    $archive.Dispose()
}
Write-Host "Prepared Node.js $nodeVersion portable runtime."
