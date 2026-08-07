[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\Codex Account Switcher"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Codex Account Switcher.lnk"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex Account Switcher.lnk"

Get-Process -Name "CodexAccountSwitcher" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CodexAccountSwitcherWindows" -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuShortcut -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $installRoot) {
    $resolved = (Resolve-Path -LiteralPath $installRoot).Path
    $expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs\Codex Account Switcher"))
    if (-not $resolved.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "예상하지 못한 제거 경로입니다: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

Write-Host "Codex Account Switcher를 제거했습니다. 계정과 Codex 세션 파일은 변경하지 않았습니다."
