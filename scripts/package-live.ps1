param([Parameter(Mandatory=$true)][string]$PlayerDirectory)
$ErrorActionPreference = 'Stop'
$player = (Resolve-Path -LiteralPath $PlayerDirectory).Path
$out = Join-Path (Split-Path $player -Parent) 'OvercookedLive-Windows-x64.zip'
$include = @('OvercookedLive.exe','OvercookedLive_Data','UnityPlayer.dll','MonoBleedingEdge',
    'LICENSE','LICENSES','THIRD_PARTY_NOTICES.md','CharacterCredits.txt','LIVE-QUICKSTART.md')
foreach ($part in @('D3D12','dstorage.dll','dstoragecore.dll','UnityCrashHandler64.exe')) {
    if (Test-Path -LiteralPath (Join-Path $player $part)) { $include += $part }
}
$files = $include | ForEach-Object { (Resolve-Path -LiteralPath (Join-Path $player $_)).Path }
Compress-Archive -LiteralPath $files -DestinationPath $out -CompressionLevel Optimal -Force
Get-FileHash -LiteralPath $out -Algorithm SHA256
