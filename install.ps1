[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot

Get-Process -Name "CodexAccountSwitcher" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 250
& (Join-Path $projectRoot "prepare-node.ps1")

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "vendor\codex-auth.exe")) -and
    -not (Get-Command codex-auth -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw "Node.js 22 이상을 먼저 설치해 주세요."
    }
    Write-Host "codex-auth를 설치합니다…"
    & npm install -g "@loongphy/codex-auth@0.2.10"
    if ($LASTEXITCODE -ne 0) { throw "codex-auth 설치에 실패했습니다." }
}

if (-not $SkipBuild) {
    & (Join-Path $projectRoot "build.ps1")
}

$sourceExe = Join-Path $projectRoot "dist\CodexAccountSwitcher.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "실행 파일이 없습니다. build.ps1을 먼저 실행해 주세요."
}

$installRoot = Join-Path $env:LOCALAPPDATA "Programs\Codex Account Switcher"
$installExe = Join-Path $installRoot "CodexAccountSwitcher.exe"
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installExe -Force
$bundledAuth = Join-Path $projectRoot "vendor\codex-auth.exe"
if (Test-Path -LiteralPath $bundledAuth) {
    Copy-Item -LiteralPath $bundledAuth -Destination (Join-Path $installRoot "codex-auth.exe") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "vendor\LICENSE-codex-auth.txt") -Destination (Join-Path $installRoot "LICENSE-codex-auth.txt") -Force
}
$nodeArchive = Join-Path $projectRoot "vendor\node-runtime.zip"
if (Test-Path -LiteralPath $nodeArchive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($nodeArchive)
    try {
        $nodeEntry = $archive.Entries | Where-Object { $_.FullName -match '/node\.exe$' } | Select-Object -First 1
        if (-not $nodeEntry) { throw "node.exe is missing from the portable Node.js archive." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($nodeEntry, (Join-Path $installRoot "node.exe"), $true)
    }
    finally { $archive.Dispose() }
    Copy-Item -LiteralPath (Join-Path $projectRoot "vendor\LICENSE-node.txt") -Destination (Join-Path $installRoot "LICENSE-node.txt") -Force
}

$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shell = New-Object -ComObject WScript.Shell

foreach ($shortcutPath in @(
    (Join-Path $desktop "Codex Account Switcher.lnk"),
    (Join-Path $startMenu "Codex Account Switcher.lnk")
)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installExe
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.Description = "Codex 계정을 안전하게 전환합니다"
    $shortcut.Save()
}

Start-Process -FilePath $installExe
Write-Host "설치 완료: $installExe"
