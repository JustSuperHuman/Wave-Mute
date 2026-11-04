param([string]$Version = '0.1.1')

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $workspace "artifacts/WaveMute-$Version-Setup.exe"
$installDir = Join-Path $workspace 'artifacts/installer smoke/WaveMute'
$executable = Join-Path $installDir 'WaveMute.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{3E396866-779A-49B2-A6F5-0334F19CF260}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Wave Mute.lnk'

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-StartupCommand {
    (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).WaveMute
}

function Install-WaveMute([string]$TaskOverride) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $workspace 'artifacts/installer-smoke.log')`"")
    if ($TaskOverride) { $arguments += "/MERGETASKS=$TaskOverride" }
    $setup = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Assert-That ($setup.ExitCode -eq 0) "Installer failed with exit code $($setup.ExitCode)."
}

function Start-WaveMute {
    $app = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 300
        Assert-That (-not $app.HasExited) 'The installed app exited unexpectedly.'
        $statusText = (& $executable --status) -join [Environment]::NewLine
        try { $status = $statusText | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($status.State) { return $app }
    }
    throw 'The installed app did not answer a status request.'
}

Assert-That (Test-Path -LiteralPath $installer) 'Build the installer before testing it.'
Assert-That (-not (Test-Path -LiteralPath $uninstallKey)) 'An installed copy already exists. Run this smoke test on a clean Windows account.'
Assert-That (-not (Get-StartupCommand)) 'A Wave Mute startup entry already exists.'
Assert-That (-not (Test-Path -LiteralPath $shortcut)) 'A Wave Mute Start menu shortcut already exists.'
$mutex = $null
try { $mutex = [Threading.Mutex]::OpenExisting('Local\WaveMute.v1') } catch [Threading.WaitHandleCannotBeOpenedException] { }
if ($mutex) {
    $mutex.Dispose()
    throw 'Wave Mute is already running. Exit it before testing the installer.'
}

try {
    Install-WaveMute
    Assert-That (Test-Path -LiteralPath $executable) 'The app was not installed.'
    Assert-That ((Get-StartupCommand) -ceq "`"$executable`"") 'Windows startup was not enabled by default with a quoted path.'
    Assert-That (Test-Path -LiteralPath $shortcut) 'The Start menu shortcut is missing.'
    Assert-That ((Get-ItemPropertyValue -LiteralPath $uninstallKey -Name DisplayVersion) -eq $Version) 'The installed version is incorrect.'
    $published = Join-Path $workspace "artifacts/publish/$Version/win-x64/WaveMute.exe"
    Assert-That ((Get-FileHash -LiteralPath $executable).Hash -eq (Get-FileHash -LiteralPath $published).Hash) 'The installed executable differs from the published build.'
    Write-Output 'PASS: fresh install, version, files, shortcut, and default Windows startup.'

    $app = Start-WaveMute
    Install-WaveMute '!startup'
    Assert-That ($app.WaitForExit(5000)) 'Upgrade did not gracefully stop the running app.'
    Assert-That (-not (Get-StartupCommand)) 'Disabling startup during upgrade left the startup entry behind.'
    Install-WaveMute
    Assert-That (-not (Get-StartupCommand)) 'Upgrade did not preserve the disabled startup preference.'
    Write-Output 'PASS: running-app upgrade, startup opt-out, and preference preservation.'

    Install-WaveMute 'startup'
    Assert-That ((Get-StartupCommand) -ceq "`"$executable`"") 'Re-enabling Windows startup failed.'
    $app = Start-WaveMute
    $remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -WindowStyle Hidden -Wait -PassThru
    Assert-That ($remove.ExitCode -eq 0) "Uninstall failed with exit code $($remove.ExitCode)."
    Assert-That ($app.WaitForExit(5000)) 'Uninstall did not gracefully stop the running app.'
    Assert-That (-not (Test-Path -LiteralPath $executable)) 'Uninstall left the app executable behind.'
    Assert-That (-not (Test-Path -LiteralPath $uninstallKey)) 'Uninstall left its registration behind.'
    Assert-That (-not (Get-StartupCommand)) 'Uninstall left the Windows startup entry behind.'
    Assert-That (-not (Test-Path -LiteralPath $shortcut)) 'Uninstall left the Start menu shortcut behind.'
    Write-Output 'PASS: startup opt-in and uninstall of a running app, including startup and shortcut cleanup.'
}
finally {
    if (Test-Path -LiteralPath $uninstaller) {
        Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -WindowStyle Hidden -Wait | Out-Null
    }
}
