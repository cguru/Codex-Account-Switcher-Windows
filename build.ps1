[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$sourceRoot = Join-Path $projectRoot "src"
$outputRoot = Join-Path $projectRoot "dist"
$assetRoot = Join-Path $projectRoot "assets"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw ".NET Framework C# compiler를 찾지 못했습니다: $compiler"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$output = Join-Path $outputRoot "CodexAccountSwitcher.exe"
$iconPath = Join-Path $assetRoot "app-icon.ico"
$gac = Join-Path $env:WINDIR "Microsoft.NET\assembly"
$wpfReferences = @(
    (Join-Path $gac "GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"),
    (Join-Path $gac "GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"),
    (Join-Path $gac "GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"),
    (Join-Path $gac "GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll")
)
foreach ($reference in $wpfReferences) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "WPF 구성 요소를 찾지 못했습니다: $reference" }
}
if (-not (Test-Path -LiteralPath $iconPath)) { throw "앱 아이콘을 찾지 못했습니다: $iconPath" }

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter "*.cs" -File |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug:pdbonly",
    "/out:$output",
    "/win32icon:$iconPath",
    "/win32manifest:$(Join-Path $sourceRoot 'app.manifest')",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Runtime.Serialization.dll",
    "/reference:System.Windows.Forms.dll"
) + ($wpfReferences | ForEach-Object { "/reference:$_" }) + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "빌드에 실패했습니다." }

Write-Host "빌드 완료: $output"
