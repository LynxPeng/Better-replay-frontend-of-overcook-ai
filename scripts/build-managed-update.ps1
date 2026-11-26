param(
    [string]$PlayerDirectory,
    [string]$OutputDirectory,
    [string]$UnityEditorRoot='C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor'
)
# Recompile application scripts for an existing, matching Mono player. This does
# not rebake scenes/assets or replace a full Unity build when those change.
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(-not $PlayerDirectory){$PlayerDirectory=Join-Path $repoRoot 'output\Windows'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repoRoot 'output\Windows-v3'}
$sourceRoot=(Resolve-Path -LiteralPath $PlayerDirectory).Path
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if($sourceRoot.TrimEnd('\') -eq $destination.TrimEnd('\')){throw 'Output must differ from the base player'}
if(Test-Path -LiteralPath $destination){throw "Refusing to overwrite an existing player: $destination"}
$managed=Join-Path $sourceRoot 'OvercookedReplay_Data\Managed'
if(-not(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'A Mono replay player is required'}
$compile=Join-Path $repoRoot 'output\v3-compile'
New-Item -ItemType Directory -Force -Path $compile | Out-Null
$compiler=Get-ChildItem -Path (Join-Path $UnityEditorRoot 'Data\DotNetSdk\sdk\*\Roslyn\bincore\csc.dll') | Select-Object -First 1
if(-not $compiler){throw 'Unity bundled Roslyn compiler not found'}
$dotnet=Join-Path $UnityEditorRoot 'Data\DotNetSdk\dotnet.exe'
$assembly=Join-Path $compile 'Assembly-CSharp.dll'
$lines=@('/nostdlib+','/target:library','/langversion:9.0','/optimize+',
    '/define:UNITY_STANDALONE_WIN;UNITY_STANDALONE;ENABLE_MONO;ENABLE_LEGACY_INPUT_MANAGER;NET_STANDARD_2_1',
    ('/out:"'+$assembly+'"'))
foreach($dll in Get-ChildItem -LiteralPath $managed -Filter '*.dll'){
    if($dll.Name -ne 'Assembly-CSharp.dll'){$lines+=('/reference:"'+$dll.FullName+'"')}
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'unity\OvercookedViewer\Assets\Scripts') -Filter '*.cs'){
    $lines+=('"'+$file.FullName+'"')
}
$lines+=('"'+(Join-Path $repoRoot 'unity\OvercookedViewer\Assets\TutorialInfo\Readme.cs')+'"')
$response=Join-Path $compile 'compile.rsp'
Set-Content -LiteralPath $response -Value $lines -Encoding utf8
& $dotnet $compiler.FullName '/noconfig' ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Application script compilation failed'}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Get-ChildItem -LiteralPath $sourceRoot | Copy-Item -Destination $destination -Recurse
Copy-Item -LiteralPath $assembly -Destination (Join-Path $destination 'OvercookedReplay_Data\Managed\Assembly-CSharp.dll') -Force
$samples=Join-Path $destination 'OvercookedReplay_Data\StreamingAssets\Samples'
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'unity\OvercookedViewer\Assets\StreamingAssets\Samples') -Filter '*.ocrep.json' |
    Copy-Item -Destination $samples -Force
Set-Content -LiteralPath (Join-Path $destination 'BUILD-METHOD.txt') -Encoding utf8 -Value @'
Application scripts recompiled for the existing Unity 6000.6.9f1 Mono player.
Base executable, Unity runtime, scenes, resources and character assemblies reused.
No scene or resource rebake. New v3 geometry is created at runtime from existing
resources and primitives. Use build-windows.ps1 with an activated Editor for a
full rebuild. See docs/V3_REPLAY.md for validation and recording instructions.
'@
Write-Output (Join-Path $destination 'OvercookedReplay.exe')
