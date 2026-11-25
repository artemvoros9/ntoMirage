# Compile-checks Assets/Radiation/Runtime against the locally installed Unity editor assemblies.
# Usage: pwsh -File Documentation/tools/compile-check.ps1
param(
    [string]$EditorVersion = "6000.5.6f1"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$editorData = "C:\Program Files\Unity\Hub\Editor\$EditorVersion\Editor\Data"
if (-not (Test-Path $editorData)) { throw "Unity editor data not found: $editorData" }

$csc = Join-Path $editorData 'DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll'
$dotnet = Join-Path $editorData 'DotNetSdk\dotnet.exe'
# Unity's own compile pipeline pairs netstandard.dll with the netfx shims.
$netstandard = Join-Path $editorData 'DotNetSdk\packs\NETStandard.Library.Ref\2.1.0\ref\netstandard2.1\netstandard.dll'
$shims = Join-Path $editorData 'NetStandard\compat\2.1.0\shims\netfx'
$scriptAssemblies = Join-Path $root 'Library\ScriptAssemblies'
$outDir = Join-Path $env:TEMP 'radiation-compile-check'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = New-Object System.Collections.Generic.List[string]
foreach ($dll in Get-ChildItem (Join-Path $editorData 'Managed\UnityEngine') -Filter *.dll) { $refs.Add($dll.FullName) }
$refs.Add($netstandard)
foreach ($dll in Get-ChildItem $shims -Filter *.dll) { $refs.Add($dll.FullName) }
foreach ($name in @('Unity.InputSystem.dll', 'UnityEngine.UI.dll', 'Unity.RenderPipelines.Core.Runtime.dll')) {
    $candidate = Join-Path $scriptAssemblies $name
    if (Test-Path $candidate) { $refs.Add($candidate) }
}

$sources = Get-ChildItem (Join-Path $root 'Assets\Radiation\Runtime') -Filter *.cs | ForEach-Object { $_.FullName }

$argList = New-Object System.Collections.Generic.List[string]
$argList.Add($csc)
$argList.Add('-nologo')
$argList.Add('-target:library')
$argList.Add('-langversion:9.0')
$argList.Add('-nullable:disable')
$argList.Add('-out:' + (Join-Path $outDir 'RadiationCheck.dll'))
$argList.Add('-define:UNITY_EDITOR;ENABLE_INPUT_SYSTEM;UNITY_6000_0_OR_NEWER')
foreach ($r in $refs) { $argList.Add('-r:' + $r) }
foreach ($s in $sources) { $argList.Add($s) }

& $dotnet @argList
exit $LASTEXITCODE
