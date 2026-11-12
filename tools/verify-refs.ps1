# Checks that every UnityEngine / Assembly-CSharp type and member referenced by the built plugin
# exists in the REAL game assemblies, so the hand-written stubs (stubs\) can't silently drift.
# Run locally after changing the stubs or the plugin; needs a game install with BepInEx (for Mono.Cecil).
#   powershell -File tools\verify-refs.ps1 [-GameDir "<LUMINES REMASTERED folder>"]
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\LUMINES REMASTERED",
    [string]$Plugin = "$PSScriptRoot\..\src\LuminesHaptics.Plugin\bin\Release\LuminesHaptics.dll"
)
$ErrorActionPreference = "Stop"
Add-Type -Path "$GameDir\BepInEx\core\Mono.Cecil.dll"

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory("$GameDir\Lumines-remastered_Data\Managed")
$resolver.AddSearchDirectory("$GameDir\BepInEx\core")
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$mod = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path $Plugin).Path, $rp)

$game = @("UnityEngine", "Assembly-CSharp")
$bad = 0; $ok = 0
foreach ($t in $mod.GetTypeReferences()) {
    if ($game -notcontains $t.Scope.Name) { continue }
    if ($null -eq $t.Resolve()) { "MISSING type   $($t.FullName)"; $bad++ } else { $ok++ }
}
foreach ($m in $mod.GetMemberReferences()) {
    $dt = $m.DeclaringType.GetElementType()
    if ($game -notcontains $dt.Scope.Name) { continue }
    if ($null -eq $m.Resolve()) { "MISSING member $($m.FullName)"; $bad++ } else { $ok++ }
}
"$ok game references resolved, $bad missing"
if ($bad -gt 0) { exit 1 }
