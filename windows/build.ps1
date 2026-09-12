# Album Cover Screen Saver, Windows port
# Builds everything and offers to run it.
#
# Do not run this file directly. Double-click BUILD.bat instead.
#
# What it does:
#   1. Checks for the .NET 8 SDK, and installs it if it is missing.
#   2. Puts the sample data in the shared folder, but only if that folder is
#      empty. From step 4 the tray app owns it and writes real history there.
#   3. Builds everything and runs the test suite.
#   4. Offers to run the screen saver or the tray app.
#   5. Copies the logs back here, so the Claude session can read them.
#
# Everything it prints is saved to build.log in this folder.

$ErrorActionPreference = 'Continue'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $here) { $here = (Get-Location).Path }

$log = Join-Path $here 'build.log'
try { Start-Transcript -Path $log -Force | Out-Null } catch { }

function Say($text) {
    Write-Host ""
    Write-Host "== $text" -ForegroundColor Cyan
}

function Stop-Here($text) {
    Write-Host ""
    Write-Host "STOPPED" -ForegroundColor Yellow
    Write-Host $text
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

# Runs a program and prints everything it says, so the log has no silent gaps.
# Note the name: never call a function after the program it wraps, or "& git"
# inside a function called Git calls the function again, forever.
function Invoke-Tool($exe, [string[]]$toolArgs) {
    $text = (& $exe @toolArgs 2>&1 | Out-String).TrimEnd()
    $code = $LASTEXITCODE
    if ($text) { Write-Host $text }
    return $code
}

# A running copy holds its own files open, so a rebuild cannot replace them.
# The error that causes names a locked dll and says nothing about the tray icon
# still sitting there, so this closes anything running first.
function Stop-RunningApps {
    $names = @('AlbumCoverScreenSaver.Tray', 'AlbumCoverScreenSaver')
    $running = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }

    Say "Closing what is already running"
    foreach ($process in $running) { Write-Host "  $($process.ProcessName)" }

    # Asked to quit rather than killed, so the tray icon is removed properly
    # instead of being left behind as a dead entry near the clock.
    try {
        $signal = [System.Threading.EventWaitHandle]::OpenExisting('AlbumCoverScreenSaverTrayQuit')
        [void]$signal.Set()
        $signal.Dispose()
    }
    catch {
        # No signal available, which just means the force path below is used.
    }

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        $running = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { break }
    }

    foreach ($process in $running) {
        Write-Host "  $($process.ProcessName) did not close on its own, stopping it." -ForegroundColor Yellow
        try { $process.Kill() } catch { }
    }

    if ($running.Count -gt 0) { Start-Sleep -Milliseconds 400 }
    Write-Host "Closed."
}

Set-Location $here
Say "Working in $here"

# --- 1. The .NET SDK --------------------------------------------------------

Say "Looking for the .NET SDK"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "The .NET SDK is not installed."

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Stop-Here @"
The .NET 8 SDK is not installed and winget is not available to install it.

Download it yourself from https://dotnet.microsoft.com/download/dotnet/8.0
Pick the SDK, not the runtime. Then run BUILD.bat again.
"@
    }

    Write-Host "Installing it now with winget. This is a few hundred megabytes"
    Write-Host "and takes several minutes. It may ask you to approve it."
    winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-package-agreements --accept-source-agreements

    Stop-Here @"
The .NET SDK was just installed, but this window does not know about it yet.

Close this window and double-click BUILD.bat again.
That second run will go all the way through.
"@
}

Invoke-Tool dotnet @('--version') | Out-Null

# --- 2. The shared folder ---------------------------------------------------

$dataFolder = Join-Path $env:LOCALAPPDATA 'AlbumCoverScreenSaver'
$archive = Join-Path $dataFolder 'archive.json'

Say "Shared folder: $dataFolder"
New-Item -ItemType Directory -Path (Join-Path $dataFolder 'art') -Force | Out-Null

if (Test-Path $archive) {
    Write-Host "It already has an archive, so the sample data is left alone."
    Write-Host "From step 4 the tray app owns this folder and writes your real"
    Write-Host "listening history into it. To go back to the sample data, delete"
    Write-Host "the folder above and run this again."
}
else {
    Write-Host "Empty, so copying the sample data in so there is something to draw."
    Copy-Item -Path (Join-Path $here 'fixtures\*.json') -Destination $dataFolder -Force
    Copy-Item -Path (Join-Path $here 'fixtures\art\*.jpg') -Destination (Join-Path $dataFolder 'art') -Force
}

$covers = @(Get-ChildItem -Path (Join-Path $dataFolder 'art') -Filter *.jpg -ErrorAction SilentlyContinue)
Write-Host "$($covers.Count) cover(s) on disk."

# --- 3. Build and test ------------------------------------------------------

Stop-RunningApps

Say "Building"
Write-Host "The first build downloads a few packages and will take a minute."

if ((Invoke-Tool dotnet @('build', 'AlbumCoverScreenSaver.sln', '--nologo')) -ne 0) {
    Stop-Here "The build failed. Tell the Claude session what this window printed."
}

Say "Running the tests"
if ((Invoke-Tool dotnet @('run', '--project', 'tests\AlbumCoverScreenSaver.Shared.Tests', '--no-build')) -ne 0) {
    Stop-Here "Tests failed. Tell the Claude session what this window printed."
}

# --- 4. Offer to run something ---------------------------------------------

$saver = Get-ChildItem -Path (Join-Path $here 'src\AlbumCoverScreenSaver.Saver\bin') `
    -Filter 'AlbumCoverScreenSaver.scr' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1

$tray = Get-ChildItem -Path (Join-Path $here 'src\AlbumCoverScreenSaver.Tray\bin') `
    -Filter 'AlbumCoverScreenSaver.Tray.exe' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1

Say "Built"
if ($saver) { Write-Host "Screen saver: $($saver.FullName)" -ForegroundColor Green }
if ($tray)  { Write-Host "Tray app:     $($tray.FullName)" -ForegroundColor Green }

Write-Host ""
Write-Host "What would you like to run?"
Write-Host "  1  The screen saver, full screen. Move the mouse to quit it."
Write-Host "  2  The tray app, which watches your music. Look for the record"
Write-Host "     icon near the clock. Right-click it to see what it is doing."
Write-Host "  Enter  neither"
$answer = Read-Host "Type 1 or 2 and press Enter"

if ($answer -eq '1' -and $saver) {
    Say "Running the screen saver"
    Start-Process -FilePath $saver.FullName -ArgumentList '/s' -Wait
    Write-Host "Back."
}
elseif ($answer -eq '2' -and $tray) {
    Say "Starting the tray app"
    Start-Process -FilePath $tray.FullName
    Write-Host "It is running now. Play something, give it a few seconds, then"
    Write-Host "right-click the record icon near the clock."
    Write-Host ""
    Write-Host "It keeps running after this window closes. Quit it from that menu."
    Write-Host ""
    Read-Host "Play some music, then press Enter to collect the log"
}

# --- 5. Bring the logs back -------------------------------------------------

Say "Logs"
foreach ($name in @('saver.log', 'tray.log')) {
    $source = Join-Path $dataFolder $name
    if (Test-Path $source) {
        Copy-Item -Path $source -Destination (Join-Path $here $name) -Force
        Write-Host ""
        Write-Host "--- $name ---" -ForegroundColor Cyan
        Get-Content (Join-Path $here $name) -Tail 40 | ForEach-Object { Write-Host "  $_" }
    }
}

# --- 6. Offer to push -------------------------------------------------------

# Pushing lives in its own script at the repo root, and being a separate thing
# to remember is exactly why it kept not happening. The build already knows the
# tests passed, which makes this the right moment to ask.
$push = Join-Path (Split-Path -Parent $here) 'setup-and-push.ps1'

if (Test-Path $push) {
    Say "GitHub"
    Write-Host "The build succeeded and the tests passed."
    Write-Host "Push it up now? Until you do, this machine holds the only copy."
    $answer = Read-Host "Type y and press Enter, or just press Enter to skip"

    if ($answer -match '^(y|yes)$') {
        # A separate process, so its own log is written properly rather than
        # colliding with this one's transcript.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $push
    }
    else {
        Write-Host ""
        Write-Host "Skipped. Run RUN-SETUP.bat when you are ready." -ForegroundColor Yellow
    }
}

try { Stop-Transcript | Out-Null } catch { }
