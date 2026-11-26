param([string]$UnityEditor,[switch]$Live)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$method=if($Live){'MacBuild.BuildLive'}else{'MacBuild.Build'}
$logName=if($Live){'macos-live-build'}else{'macos-build'}
& (Join-Path $PSScriptRoot 'invoke-unity-build.ps1') -UnityEditor $UnityEditor -Target OSXUniversal -Method $method -LogName $logName -SuccessMessage 'macOS Universal Mono player built:'
Write-Output (Join-Path $repoRoot $(if($Live){'output/macOS/OvercookedLive.app'}else{'output/macOS/OvercookedReplay.app'}))
