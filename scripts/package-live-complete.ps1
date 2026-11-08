param(
    [Parameter(Mandatory=$true)][string]$ClientDirectory,
    [Parameter(Mandatory=$true)][string]$HostDirectory,
    [Parameter(Mandatory=$true)][string]$Workspace,
    [Parameter(Mandatory=$true)][string]$PyInstallerPath,
    [string]$OutputDirectory,
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+[a-z0-9-]*$')][string]$Version='v0.2.0-live-windows-preview'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$workspacePath=(Resolve-Path -LiteralPath $Workspace).Path
$client=(Resolve-Path -LiteralPath $ClientDirectory).Path
$server=(Resolve-Path -LiteralPath $HostDirectory).Path
$prefix=Join-Path $workspacePath '.conda'
$models=Join-Path $workspacePath 'output\live-models'
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $workspacePath 'output\live-release'}
$bundle=Join-Path $output "OvercookedLive-Windows-x64-$Version"
$archive=$bundle+'.zip'
if((Test-Path -LiteralPath $bundle) -or (Test-Path -LiteralPath $archive)) {
    throw "Release bundle already exists: $bundle"
}
$required=@('OvercookedLive.exe','OvercookedLive_Data','UnityPlayer.dll','MonoBleedingEdge',
    'LICENSE','LICENSES','THIRD_PARTY_NOTICES.md','CharacterCredits.txt')
foreach($name in $required){(Resolve-Path -LiteralPath (Join-Path $client $name)).Path | Out-Null}
foreach($path in @((Join-Path $server 'OvercookedHost.exe'),(Join-Path $models 'ppo-final.zip'),
    (Join-Path $models 'sp-cramped-room.npz'))) {
    (Resolve-Path -LiteralPath $path).Path | Out-Null
}
New-Item -ItemType Directory -Force -Path $bundle | Out-Null
foreach($name in ($required+@('D3D12','dstorage.dll','dstoragecore.dll','UnityCrashHandler64.exe'))) {
    $path=Join-Path $client $name
    if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination $bundle -Recurse}
}
$runtime=Join-Path $bundle 'HostRuntime'
Copy-Item -LiteralPath $server -Destination $runtime -Recurse
$installedModels=Join-Path $runtime 'Models'
New-Item -ItemType Directory -Path $installedModels | Out-Null
foreach($name in @('ppo-final.zip','sp-cramped-room.npz')) {
    Copy-Item -LiteralPath (Join-Path $models $name) -Destination $installedModels
}
Copy-Item -LiteralPath (Join-Path $repo 'LIVE-PORTABLE-QUICKSTART.md') -Destination (Join-Path $bundle '开始游戏.md')
Copy-Item -LiteralPath (Join-Path $workspacePath 'LICENSE') -Destination (Join-Path $bundle 'LICENSES\Overcooked-AI.txt')
$notices=Join-Path $bundle 'LICENSES\HostRuntime'
New-Item -ItemType Directory -Path $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $prefix 'LICENSE_PYTHON.txt') -Destination $notices
foreach($packages in @((Join-Path $prefix 'Lib\site-packages'),(Resolve-Path -LiteralPath $PyInstallerPath).Path)) {
    foreach($distribution in (Get-ChildItem -LiteralPath $packages -Directory -Filter '*.dist-info')) {
        $files=Get-ChildItem -LiteralPath $distribution.FullName -Recurse -File | Where-Object {
            $_.Name -match '^(LICENSE|COPYING|NOTICE|AUTHORS)' -or $_.FullName -match '[\\/]licenses[\\/]'
        }
        foreach($file in $files) {
            $relative=$file.FullName.Substring($packages.Length+1)
            $destination=Join-Path $notices $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
}
# Frozen DLLs may come from conda packages rather than Python wheels.
$dllNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($dll in (Get-ChildItem -LiteralPath $server -Recurse -File -Filter '*.dll')) {
    [void]$dllNames.Add($dll.Name)
}
foreach($metadata in (Get-ChildItem -LiteralPath (Join-Path $prefix 'conda-meta') -File -Filter '*.json')) {
    $distribution=Get-Content -LiteralPath $metadata.FullName -Raw | ConvertFrom-Json
    $bundledFiles=$distribution.files | Where-Object { $dllNames.Contains([IO.Path]::GetFileName($_)) }
    if(!$bundledFiles){continue}
    $licensePath=Join-Path $distribution.extracted_package_dir 'info\licenses'
    if(!(Test-Path -LiteralPath $licensePath -PathType Container)) {
        throw "Missing native runtime license in the conda package cache: $($distribution.name)"
    }
    $destination=Join-Path $notices ('conda-'+$metadata.BaseName)
    Copy-Item -LiteralPath $licensePath -Destination $destination -Recurse
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive+'.sha256') -Value ($hash+'  '+(Split-Path $archive -Leaf)) -Encoding ascii
Write-Output $archive
Write-Output $hash
