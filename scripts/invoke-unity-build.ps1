param([string]$UnityEditor,[string]$Target,[string]$Method,[string]$LogName,[string]$SuccessMessage)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$projectPath=Join-Path $repoRoot 'unity\OvercookedViewer'
$versionFile=Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'
$projectVersion=((Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(.+)$').Matches.Groups[1].Value).Trim()
if(-not $UnityEditor){$UnityEditor=Join-Path $env:ProgramFiles ('Unity\Hub\Editor\'+$projectVersion+'\Editor\Unity.exe')}
if(-not(Test-Path -LiteralPath $UnityEditor)){throw "Unity $projectVersion not found. Pass -UnityEditor with the matching Unity.exe path."}
$UnityEditor=(Resolve-Path -LiteralPath $UnityEditor).Path
$actualVersion=(Get-Item -LiteralPath $UnityEditor).VersionInfo.ProductVersion
if($actualVersion -notlike ($projectVersion+'_*') -and $actualVersion -ne $projectVersion){throw "Project requires $projectVersion but selected Editor is $actualVersion"}
foreach($relative in @('Assets\Resources\RestaurantBits\Models\food_ingredient_onion.fbx','Assets\Resources\CartoonTown\Models\Trees\Tree_Simple_Green.fbx','Assets\SimpleSky\Prefabs\SkyDome.prefab')) {
    if(-not(Test-Path -LiteralPath (Join-Path $projectPath $relative))){throw "Missing licensed local art: $relative. See README Source Build; released ZIP does not need these files."}
}
New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'output') | Out-Null
$latestLog=Join-Path $repoRoot ("output/"+$LogName+".log")
$buildLog=Join-Path $repoRoot ('output/'+$LogName+'-'+[guid]::NewGuid().ToString('N')+'.log')
# Hub's Store package keeps its licensed state in its MSIX app context. Its
# bundled licensing client can also be older than the selected Editor's client.
# Use the selected Editor's versioned IPC channel, without stopping Hub or
# copying/re-activating license files.
$client=Join-Path (Split-Path $UnityEditor -Parent) 'Data\Resources\Licensing\Client\Unity.Licensing.Client.exe'
$clientVersion=((Get-Item -LiteralPath $client).VersionInfo.ProductVersion -split '\+')[0]
$channel='LicenseClient-'+$env:USERNAME+'-'+$clientVersion
$arguments=@('-batchmode','-buildTarget',$Target,'-projectPath',('"'+$projectPath+'"'),'-licensingIpc',$channel,
    '-executeMethod',$method,'-logFile',('"'+$buildLog+'"'),'-quit')
$hubPackage=if(Get-Command Get-AppxPackage -ErrorAction SilentlyContinue){Get-AppxPackage -Name 'UnityTechnologies.UnityHub' | Select-Object -First 1}
Write-Output "Editor: $UnityEditor ($actualVersion); licensing client $clientVersion"
try{if($hubPackage){
    if(-not(Get-Command Invoke-CommandInDesktopPackage -ErrorAction SilentlyContinue)){throw 'Store Hub detected but Appx package launch support is unavailable'}
    Invoke-CommandInDesktopPackage -PackageFamilyName $hubPackage.PackageFamilyName -AppId 'UnityHub' -Command $UnityEditor -Args ($arguments -join ' ') -PreventBreakaway
    $process=$null
    $startDeadline=[DateTime]::UtcNow.AddSeconds(30)
    while(-not $process -and [DateTime]::UtcNow -lt $startDeadline){
        $candidate=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($buildLog)} | Select-Object -First 1
        if($candidate){$process=Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue}
        if(-not $process -and (Test-Path -LiteralPath $buildLog)){
            if((Get-Content -LiteralPath $buildLog -Raw) -match 'Application will exit with return code -?\d+'){break}
        }
        if(-not $process){Start-Sleep -Milliseconds 100}
    }
    $exitCode=$null
    if($process){[void]$process.Handle;$process.WaitForExit();$exitCode=$process.ExitCode}
}else{
    $process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    $exitCode=$process.ExitCode
}
    if(-not(Test-Path -LiteralPath $buildLog)){throw "Unity did not start within the deadline; inspect $buildLog"}
    $logText=Get-Content -LiteralPath $buildLog -Raw
    $exitRecords=[regex]::Matches($logText,'Application will exit with return code (-?\d+)')
    if(($null -ne $exitCode -and $exitCode -ne 0) -or $exitRecords.Count -eq 0 -or
       $exitRecords[$exitRecords.Count-1].Groups[1].Value -ne '0' -or
       $logText -notmatch $SuccessMessage){
        throw "Unity build did not complete successfully; inspect $buildLog"
    }
}finally{if(Test-Path -LiteralPath $buildLog){Copy-Item -LiteralPath $buildLog -Destination $latestLog -Force}}
