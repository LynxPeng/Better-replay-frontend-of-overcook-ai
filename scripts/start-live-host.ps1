param(
    [string]$Workspace,
    [ValidateRange(0,65535)][int]$Port = 8765,
    [ValidateSet('0.0.0.0','127.0.0.1')][string]$Bind = '0.0.0.0',
    [switch]$Reopen,
    [string]$ClientDirectory,
    [string]$SmokeReport,
    [string]$Conda = 'C:\Runtime\miniconda3\Scripts\conda.exe'
)
$ErrorActionPreference = 'Stop'
$frontend = Split-Path $PSScriptRoot -Parent
if (!$Workspace) { $Workspace = Split-Path $frontend -Parent }
$Workspace = (Resolve-Path -LiteralPath $Workspace).Path
$prefix = Join-Path $Workspace '.conda'
$output = Join-Path $Workspace 'output'
$controlDir = Join-Path $output 'live-control'
if ($Reopen) {
    if ($Port -eq 0) { throw 'Use the actual port printed by the room launcher.' }
    $activeFile = Join-Path $controlDir "$Port.json"
    if (!(Test-Path -LiteralPath $activeFile -PathType Leaf)) { throw "No active launcher on port $Port." }
    $active = Get-Content -LiteralPath $activeFile -Raw | ConvertFrom-Json
    if (!(Get-Process -Id $active.launcher_pid -ErrorAction SilentlyContinue)) { throw 'The original room launcher has stopped.' }
    [IO.File]::WriteAllText((Join-Path $controlDir "$Port.reopen"), $active.session_id)
    Write-Host "Requested a host window on port $Port."
    return
}
if (!$ClientDirectory) { $ClientDirectory = Join-Path $output 'Live' }
$client = Join-Path (Resolve-Path -LiteralPath $ClientDirectory).Path 'OvercookedLive.exe'
$models = Join-Path $output 'live-models'
$records = Join-Path $output 'live-records'
foreach ($required in @($Conda, (Join-Path $prefix 'python.exe'), $client)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file missing: $required" }
}
$sessionId = [guid]::NewGuid().ToString('N')
$session = Join-Path ([IO.Path]::GetTempPath()) ('OvercookedLive-' + $sessionId)
[IO.Directory]::CreateDirectory($session) | Out-Null
$tokenFile = Join-Path $session 'token'
$readyFile = Join-Path $session 'ready.json'
$stopFile = Join-Path $session 'stop'
$originalPath = $env:PYTHONPATH
$originalToken = $env:BETTER_REPLAY_HOST_TOKEN
$originalReplay = $env:BETTER_REPLAY_REPLAY_EXE
$server = $null
$activeFile = $null
function Start-HostPlayer {
    $flags = @('--live-address','127.0.0.1','--live-port',[string]$actualPort)
    if ($SmokeReport) {
        $flags += @('--live-smoke','ha','--live-smoke-steps','8','--live-smoke-rate','6',
                    '--live-smoke-report',$SmokeReport,'-logFile', "$SmokeReport.log")
    }
    $quotedFlags = $flags | ForEach-Object { '"' + $_ + '"' }
    $env:BETTER_REPLAY_HOST_TOKEN = $token
    try {
        if ($SmokeReport) {
            return Start-Process -FilePath $client -ArgumentList $quotedFlags -PassThru -WindowStyle Hidden
        }
        return Start-Process -FilePath $client -ArgumentList $quotedFlags -PassThru
    } finally { $env:BETTER_REPLAY_HOST_TOKEN = $originalToken }
}
try {
    if ($SmokeReport) {
        $SmokeReport = [IO.Path]::GetFullPath($SmokeReport)
        if (Test-Path -LiteralPath $SmokeReport) { throw "Refusing to overwrite $SmokeReport" }
        [IO.Directory]::CreateDirectory((Split-Path $SmokeReport -Parent)) | Out-Null
    }
    $env:PYTHONPATH = (Join-Path $frontend 'src') + [IO.Path]::PathSeparator + (Join-Path $Workspace 'src')
    [IO.Directory]::CreateDirectory($models) | Out-Null
    [IO.Directory]::CreateDirectory($records) | Out-Null
    $sources = @()
    $checkpoint = Join-Path $Workspace 'src\overcooked_demo\server\static\assets\agents\RllibCrampedRoomSP\agent\checkpoint-550'
    if (Test-Path -LiteralPath $checkpoint -PathType Leaf) {
        $sources += @('--upstream',$Workspace)
    }
    $ppo = Join-Path $output 'ppo-final\policy.zip'
    if ((Test-Path -LiteralPath $ppo) -and !(Test-Path -LiteralPath (Join-Path $models 'ppo-final.zip'))) {
        $sources += @('--sb3',$ppo,'--id','ppo-final')
    }
    if ($sources.Count -gt 0) {
        & $Conda run --no-capture-output --prefix $prefix python -m better_overcooked_replay.live_policy prepare --models $models @sources
        if ($LASTEXITCODE -ne 0) { throw 'Could not prepare local model catalog.' }
    }

    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $token = [Convert]::ToBase64String($bytes)
    [IO.File]::WriteAllText($tokenFile,$token)
    $argsForServer = @('run','--no-capture-output','--prefix',$prefix,'python','-u','-m',
        'better_overcooked_replay.live_server','--bind',$Bind,'--port',[string]$Port,
        '--host-token-file',$tokenFile,'--ready-file',$readyFile,'--stop-file',$stopFile,
        '--models',$models,'--records',$records)
    $quoted = $argsForServer | ForEach-Object { '"' + $_ + '"' }
    $logId = [guid]::NewGuid().ToString('N')
    $errorLog = Join-Path $output ("live-server-$logId.err.log")
    $server = Start-Process -FilePath $Conda -ArgumentList $quoted -WorkingDirectory $Workspace -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $output ("live-server-$logId.log")) -RedirectStandardError $errorLog
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (!(Test-Path -LiteralPath $readyFile)) {
        if ($server.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw "Room could not start. See $errorLog" }
        Start-Sleep -Milliseconds 200
    }
    $actualPort = (Get-Content -Raw -LiteralPath $readyFile | ConvertFrom-Json).port
    [IO.Directory]::CreateDirectory($controlDir) | Out-Null
    $activeFile = Join-Path $controlDir "$actualPort.json"
    $reopenFile = Join-Path $controlDir "$actualPort.reopen"
    $active = @{ launcher_pid=$PID; session_id=$sessionId; port=$actualPort }
    [IO.File]::WriteAllText($activeFile, ($active | ConvertTo-Json))
    $env:BETTER_REPLAY_REPLAY_EXE = Join-Path $output 'Windows\OvercookedReplay.exe'
    $player = Start-HostPlayer
    Write-Host "Host client PID $($player.Id). Port $actualPort. Press R here to reopen a closed host window, Ctrl+C to save and stop."
    Write-Host "From a second terminal: .\scripts\start-live-host.ps1 -Reopen -Port $actualPort"
    $keyboard = [Environment]::UserInteractive -and ![Console]::IsInputRedirected
    $smokeDeadline = [DateTime]::UtcNow.AddMinutes(3)
    while (!$server.WaitForExit(200)) {
        if ($SmokeReport) {
            if ($player.HasExited) {
                if ($player.ExitCode -ne 0 -or !(Test-Path -LiteralPath $SmokeReport)) { throw "Player check failed. See $SmokeReport.log" }
                $report = Get-Content -Raw -LiteralPath $SmokeReport | ConvertFrom-Json
                if (!$report.ok -or !$report.host -or !$report.complete -or $report.tick -ne 8) { throw 'Player did not complete the expected host session.' }
                break
            }
            if ([DateTime]::UtcNow -ge $smokeDeadline) { throw "Player check timed out. See $SmokeReport.log" }
        }
        $relaunch = $false
        if ($keyboard) {
            try {
                if ([Console]::KeyAvailable) { $relaunch = [Console]::ReadKey($true).Key -eq [ConsoleKey]::R }
            } catch [InvalidOperationException] { $keyboard = $false }
        }
        if (Test-Path -LiteralPath $reopenFile) {
            $relaunch = ([IO.File]::ReadAllText($reopenFile) -eq $sessionId) -or $relaunch
            [IO.File]::Delete($reopenFile)
        }
        if ($relaunch -and $player.HasExited) {
            $player = Start-HostPlayer
            Write-Host "Reopened host window, PID $($player.Id), port $actualPort."
        }
    }
    if ($server.HasExited -and ($server.ExitCode -ne 0 -or $SmokeReport)) { throw "Room stopped unexpectedly. See $errorLog" }
} finally {
    $env:PYTHONPATH = $originalPath
    $env:BETTER_REPLAY_HOST_TOKEN = $originalToken
    $env:BETTER_REPLAY_REPLAY_EXE = $originalReplay
    if ($server -and !$server.HasExited) {
        [IO.File]::WriteAllText($stopFile,'stop')
        if (!$server.WaitForExit(100000)) { Write-Warning "Room still shutting down. Stop request retained in $session." }
    }
    if ($activeFile -and [IO.File]::Exists($activeFile)) {
        $active = Get-Content -Raw -LiteralPath $activeFile | ConvertFrom-Json
        if ($active.session_id -eq $sessionId) { [IO.File]::Delete($activeFile); [IO.File]::Delete($reopenFile) }
    }
    if (!$server -or $server.HasExited) {
        foreach ($file in @($tokenFile,$readyFile,$stopFile)) {
            if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
        }
        if ([IO.Directory]::Exists($session) -and ![IO.Directory]::GetFileSystemEntries($session).Length) {
            [IO.Directory]::Delete($session)
        }
    }
}
