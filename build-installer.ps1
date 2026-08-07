[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
& (Join-Path $projectRoot "build.ps1")
& (Join-Path $projectRoot "prepare-vendor.ps1")
& (Join-Path $projectRoot "prepare-node.ps1")

$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$output = Join-Path $projectRoot "dist\CodexAccountSwitcher-Setup.exe"
$setupSource = Join-Path $projectRoot "installer\Setup.cs"
$appPayload = Join-Path $projectRoot "dist\CodexAccountSwitcher.exe"
$authPayload = Join-Path $projectRoot "vendor\codex-auth.exe"
$licensePayload = Join-Path $projectRoot "vendor\LICENSE-codex-auth.txt"
$nodePayload = Join-Path $projectRoot "vendor\node-runtime.zip"
$nodeLicense = Join-Path $projectRoot "vendor\LICENSE-node.txt"
$icon = Join-Path $projectRoot "assets\app-icon.ico"

foreach ($required in @($compiler, $setupSource, $appPayload, $authPayload, $licensePayload, $nodePayload, $nodeLicense, $icon)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "설치 프로그램 구성 파일을 찾지 못했습니다: $required" }
}

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$output",
    "/win32icon:$icon",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:Microsoft.CSharp.dll",
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll",
    "/resource:$appPayload,SwitcherPayload",
    "/resource:$authPayload,CodexAuthPayload",
    "/resource:$licensePayload,CodexAuthLicense",
    "/resource:$nodePayload,NodePayloadZip",
    "/resource:$nodeLicense,NodeLicense",
    $setupSource
)

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 빌드에 실패했습니다." }
Write-Host "설치 프로그램 생성 완료: $output"
