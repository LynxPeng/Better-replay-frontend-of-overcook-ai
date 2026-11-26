param([string]$UnityEditor, [switch]$Initialize)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$projectPath=Join-Path $repoRoot 'unity\OvercookedViewer'
$method=if($Initialize){'ProjectBootstrap.Build'}else{'ReplayUiChecks.Build'}
& (Join-Path $PSScriptRoot 'invoke-unity-build.ps1') -UnityEditor $UnityEditor -Target Win64 -Method $method -LogName 'windows-build' -SuccessMessage '(Native dialog fixed player built:|Windows player built:)'
& (Join-Path $PSScriptRoot 'build-shared-trajectory.ps1')
Write-Output (Join-Path $repoRoot 'output\Windows\OvercookedReplay.exe')
