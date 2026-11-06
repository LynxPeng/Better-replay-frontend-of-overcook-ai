param([Parameter(Mandatory=$true)][string]$PlayerDirectory,[string]$Version='v0.1.0')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$playerRoot=(Resolve-Path -LiteralPath $PlayerDirectory).Path
foreach($required in @('OvercookedReplay.exe','UnityPlayer.dll','OvercookedReplay_Data','MonoBleedingEdge')){if(-not(Test-Path -LiteralPath (Join-Path $playerRoot $required))){throw "Incomplete player: $required"}}
$outputRoot=Join-Path $repoRoot 'release'
$bundleRoot=Join-Path $outputRoot ('BetterReplay-Windows-x64-'+$Version)
if(Test-Path -LiteralPath $bundleRoot){throw "Refusing to overwrite existing bundle: $bundleRoot"}
New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
foreach($entry in @('OvercookedReplay.exe','UnityPlayer.dll','UnityCrashHandler64.exe','dstorage.dll','dstoragecore.dll','OvercookedReplay_Data','MonoBleedingEdge','D3D12')){
    $sourcePath=Join-Path $playerRoot $entry
    if(Test-Path -LiteralPath $sourcePath){Copy-Item -LiteralPath $sourcePath -Destination $bundleRoot -Recurse}
}
foreach($entry in @('LICENSE','LICENSES','THIRD_PARTY_NOTICES.md','README.md')){Copy-Item -LiteralPath (Join-Path $repoRoot $entry) -Destination $bundleRoot -Recurse}
Copy-Item -LiteralPath (Join-Path $repoRoot 'WINDOWS-QUICKSTART.md') -Destination (Join-Path $bundleRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'unity\OvercookedViewer\Assets\StreamingAssets\CharacterCredits.txt') -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'unity\OvercookedViewer\Assets\StreamingAssets\CharacterCredits.txt') -Destination (Join-Path $bundleRoot 'OvercookedReplay_Data\StreamingAssets\CharacterCredits.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'unity\OvercookedViewer\Assets\StreamingAssets\Samples\upstream-sp-220.ocrep.json') -Destination (Join-Path $bundleRoot 'OvercookedReplay_Data\StreamingAssets\Samples\upstream-sp-220.ocrep.json')
$archivePath=$bundleRoot+'.zip'
Compress-Archive -LiteralPath $bundleRoot -DestinationPath $archivePath -CompressionLevel Optimal
$hash=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archivePath+'.sha256') -Value ($hash+'  '+(Split-Path $archivePath -Leaf)) -Encoding ascii
Write-Output $archivePath
