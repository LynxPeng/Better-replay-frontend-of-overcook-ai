param(
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe',
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$OutputRoot) { $OutputRoot = Join-Path $repo 'output' }
$output = [IO.Path]::GetFullPath($OutputRoot)
$buildDir = Join-Path $output 'Live'
$project = Join-Path $repo 'unity\OvercookedViewer'
foreach ($asset in @('Assets\Resources\CartoonTown\Models\Trees\Tree_Simple_Green.fbx',
    'Assets\Resources\RestaurantBits\Models\food_ingredient_onion.fbx',
    'Assets\SimpleSky\Prefabs\SkyDome.prefab','Assets\Resources\LocalCharacters\BlueFatFish.prefab',
    'Assets\Resources\LocalCharacters\GuguGaga.prefab')) {
    if (!(Test-Path -LiteralPath (Join-Path $project $asset))) { throw "Missing local asset: $asset" }
}
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$oldTarget = $env:BETTER_REPLAY_LIVE_BUILD
try {
    $env:BETTER_REPLAY_LIVE_BUILD = Join-Path $buildDir 'OvercookedLive.exe'
    $log = Join-Path $output 'live-build.log'
    $process = Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode','-buildTarget','Win64',
        '-projectPath',('"' + $project + '"'),'-executeMethod','LiveBuild.Build','-quit',
        '-logFile',('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $env:BETTER_REPLAY_LIVE_BUILD)) {
        throw "Unity build failed. See $log"
    }
    foreach ($name in @('LICENSE','LICENSES','THIRD_PARTY_NOTICES.md','LIVE-QUICKSTART.md')) {
        Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $buildDir -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $project 'Assets\StreamingAssets\CharacterCredits.txt') -Destination $buildDir -Force
    Write-Output $env:BETTER_REPLAY_LIVE_BUILD
} finally { $env:BETTER_REPLAY_LIVE_BUILD = $oldTarget }
