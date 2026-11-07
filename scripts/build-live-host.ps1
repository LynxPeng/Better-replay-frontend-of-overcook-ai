param(
    [Parameter(Mandatory=$true)][string]$Workspace,
    [Parameter(Mandatory=$true)][string]$BuildRoot,
    [Parameter(Mandatory=$true)][string]$PyInstallerPath,
    [string]$Conda='C:\Runtime\miniconda3\Scripts\conda.exe'
)
$ErrorActionPreference='Stop'
$workspacePath=(Resolve-Path -LiteralPath $Workspace).Path
$repo=Split-Path $PSScriptRoot -Parent
$build=[IO.Path]::GetFullPath($BuildRoot)
if(Test-Path -LiteralPath $build){throw "Choose a new host build directory: $build"}
New-Item -ItemType Directory -Path $build | Out-Null
$upstream=Join-Path $workspacePath 'src'
$layouts=Join-Path $upstream 'overcooked_ai_py\data\layouts'
$prefix=Join-Path $workspacePath '.conda'
$previous=$env:PYTHONPATH
try {
    $env:PYTHONPATH=(Resolve-Path -LiteralPath $PyInstallerPath).Path
    $options=@('--onedir','--console','--noupx','--name','OvercookedHost',
        '--distpath',$build,'--workpath',(Join-Path $build 'work'),'--specpath',$build,
        '--paths',(Join-Path $repo 'src'),'--paths',$upstream,
        '--add-data',($layouts+';overcooked_ai_py/data/layouts'),
        '--collect-data','stable_baselines3',
        '--runtime-hook',(Join-Path $PSScriptRoot 'live-host-runtime-hook.py'))
    foreach($module in @('pytest','notebook','tensorboard','torchvision','torchaudio','tensorflow','jax')) {
        $options+=@('--exclude-module',$module)
    }
    & $Conda run --no-capture-output --prefix $prefix python -m PyInstaller @options (Join-Path $PSScriptRoot 'live-host-entry.py')
    if($LASTEXITCODE){throw 'Frozen host build failed'}
    $hostDirectory=Join-Path $build 'OvercookedHost'
    # The upstream environment constructs the legacy sprite sheet loader at import time.
    $graphics=Join-Path $upstream 'overcooked_ai_py\data\graphics'
    Copy-Item -LiteralPath $graphics -Destination (Join-Path $hostDirectory '_internal\overcooked_ai_py\data') -Recurse
    Write-Output $hostDirectory
} finally {
    $env:PYTHONPATH=$previous
}
