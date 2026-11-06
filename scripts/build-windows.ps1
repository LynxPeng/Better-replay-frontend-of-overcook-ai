param([string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe', [switch]$Initialize)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$projectPath=Join-Path $repoRoot 'unity\OvercookedViewer'
foreach($relative in @('Assets\Resources\RestaurantBits\Models\food_ingredient_onion.fbx','Assets\Resources\CartoonTown\Models\Trees\Tree_Simple_Green.fbx','Assets\SimpleSky\Prefabs\SkyDome.prefab')) {
    if(-not(Test-Path -LiteralPath (Join-Path $projectPath $relative))){throw "Missing licensed local art: $relative. See README Source Build; released ZIP does not need these files."}
}
New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'output') | Out-Null
$method=if($Initialize){'ProjectBootstrap.Build'}else{'ReplayUiChecks.Build'}
$buildLog=Join-Path $repoRoot 'output\windows-build.log'
$process=Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode','-projectPath',('"'+$projectPath+'"'),'-executeMethod',$method,'-logFile',('"'+$buildLog+'"'),'-quit') -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0){throw "Unity build failed; inspect $buildLog"}
Write-Output (Join-Path $repoRoot 'output\Windows\OvercookedReplay.exe')
