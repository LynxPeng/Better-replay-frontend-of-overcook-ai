param([string]$EnvironmentRepository, [string]$Python)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if (-not $EnvironmentRepository) { $EnvironmentRepository=Join-Path (Split-Path $repo -Parent) 'hucooked' }
if (-not $Python) { $Python=Join-Path $repo '.conda\python.exe' }
$work=Join-Path $repo 'output\shared-trajectory-build'
$dist=Join-Path $repo 'output\Windows'
New-Item -Type Directory -Force -Path $work,$dist | Out-Null
# Snapshot fingerprints hash the three source files. Ship those exact bytes with
# the external engine; never read or execute Python files from a replay folder.
$versions=@{hy_current='de37e0818c2e76311e105ab8689e5162bd84133f';hy_fixed='10aba7415fb0ce8340b2cc99025ca1f0627a92e0'}
$options=@('--noconfirm','--onedir','--noupx','--name','SharedTrajectory','--distpath',$dist,
    '--workpath',(Join-Path $work 'work'),'--specpath',$work,'--paths',$work)
foreach($entry in $versions.GetEnumerator()) {
    $package=Join-Path $work $entry.Key
    New-Item -Type Directory -Force -Path $package | Out-Null
    $zip=Join-Path $work ($entry.Key+'.zip')
    git -C $EnvironmentRepository archive --format=zip -o $zip $entry.Value src/overcooked_ai_py/cooperative/shared LICENSE
    if ($LASTEXITCODE) { throw "Missing HuCooked environment commit $($entry.Value) in $EnvironmentRepository" }
    Expand-Archive -LiteralPath $zip -DestinationPath $package -Force
    $src=Join-Path $package 'src\overcooked_ai_py\cooperative\shared'
    foreach($file in @('__init__.py','model.py','layouts.py','kitchen.py')) {
        Copy-Item -LiteralPath (Join-Path $src $file) -Destination (Join-Path $package $file) -Force
        $options+=@('--add-data',((Join-Path $package $file)+';'+$entry.Key))
    }
    $options+=@('--add-data',((Join-Path $package 'LICENSE')+';'+$entry.Key))
}
foreach($mod in @('scipy','matplotlib','torch','pandas','pytest','PyQt5','IPython','cv2')) {
    $options+=@('--exclude-module',$mod)
}
& $Python -m PyInstaller @options (Join-Path $PSScriptRoot 'shared-trajectory.py')
if ($LASTEXITCODE) { throw 'Failed to package the shared kitchen trajectory reader' }
$internal=Join-Path $dist 'SharedTrajectory\_internal'
$prefix=Split-Path $Python -Parent
$pythonLicense=@('LICENSE_PYTHON.txt','LICENSE.txt') | ForEach-Object { Join-Path $prefix $_ } | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $internal 'Python-LICENSE.txt') -Force
$numpyLicense=Get-Item (Join-Path $prefix 'Lib\site-packages\numpy-*.dist-info\LICENSE.txt')
Copy-Item -LiteralPath $numpyLicense.FullName -Destination (Join-Path $internal 'NumPy-LICENSE.txt') -Force
$bootLicense=Get-Item (Join-Path $prefix 'Lib\site-packages\pyinstaller-*.dist-info\licenses\COPYING.txt')
Copy-Item -LiteralPath $bootLicense.FullName -Destination (Join-Path $internal 'PyInstaller-COPYING.txt') -Force
