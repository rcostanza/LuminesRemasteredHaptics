# Builds the drag-and-drop release zip: dist\LuminesHaptics-v<version>.zip
#   pwsh tools\build-release.ps1 [-Version 0.2] [-BepInExCoreDir <dir with BepInEx.dll + 0Harmony.dll>]
# The zip mirrors the game folder: users drop its BepInEx\ onto the game dir (BepInEx itself not included).
# Game assemblies come from stubs\, so no game install is needed. BepInExCoreDir defaults to the
# BepInEx\core of a local game install. CI points it at the official BepInEx release zip instead.
param(
    [string]$Version,
    [string]$BepInExCoreDir = "C:\Program Files (x86)\Steam\steamapps\common\LUMINES REMASTERED\BepInEx\core"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $Version = ([xml](Get-Content "$root\src\LuminesHaptics.Plugin\LuminesHaptics.Plugin.csproj" -Raw)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
# BepInEx parses the plugin version with System.Version: digits and dots only.
if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}$') { throw "Invalid version '$Version' (expected e.g. 0.1 or 1.23)" }
$version = $Version
foreach ($f in "BepInEx.dll", "0Harmony.dll") {
    if (-not (Test-Path (Join-Path $BepInExCoreDir $f))) { throw "$f not found in $BepInExCoreDir" }
}

dotnet build "$root\src\LuminesHaptics.Plugin" -c Release -nologo "-p:Version=$version" "-p:BepInExCoreDir=$BepInExCoreDir"
if ($LASTEXITCODE -ne 0) { throw "build failed: plugin" }
dotnet build "$root\src\LuminesHaptics.Helper" -c Release -nologo "-p:Version=$version"
if ($LASTEXITCODE -ne 0) { throw "build failed: helper" }

$stage = "$root\dist\staging"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$plug = New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\LuminesHaptics"
$help = New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\LuminesHaptics\Helper"

Copy-Item "$root\src\LuminesHaptics.Plugin\bin\Release\LuminesHaptics.dll" $plug
(Get-Content "$root\packaging\README.txt" -Raw).Replace("{VERSION}", $version) |
    Set-Content "$plug\README.txt" -Encoding UTF8
$hb = "$root\src\LuminesHaptics.Helper\bin\Release"
Copy-Item "$hb\LuminesHapticsHelper.exe", "$hb\LuminesHapticsHelper.exe.config", "$hb\NAudio.Core.dll", "$hb\NAudio.Wasapi.dll" $help
"NAudio (https://github.com/naudio/NAudio), MIT license:`r`n`r`n" + (Get-Content "$root\third_party\NAudio-LICENSE.txt" -Raw) |
    Set-Content "$help\THIRD-PARTY-NOTICES.txt" -Encoding UTF8

$zip = "$root\dist\LuminesHaptics-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Built $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) + "  (" + $_.Length + " B)" }
