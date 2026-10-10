# Exercises the launcher's client-update path end to end without clicking the UI.
#
# The LAUNCH button sits in the header at a fractional position that keeps moving
# when the layout changes, and UIPI blocks synthetic clicks from a
# non-elevated process. Calling ClientFilesUpdater directly tests the part that
# actually matters: download, SHA-256 verification against the master, extraction
# into bin\, and the resulting file layout.
#
# Run under Windows PowerShell 5.1 so the .NET Framework 4.8 launcher
# assembly loads, and elevated, because the launcher requires administrator:
#   powershell -ExecutionPolicy Bypass -File tools\verify-client-update.ps1

param(
    [string]$LauncherDirectory = 'F:\GTMP\GTMP-Launcher\src\GTANetworkLauncher.WPF\bin\Debug'
)

$ErrorActionPreference = 'Stop'

$launcherAssembly = Join-Path $LauncherDirectory 'GrandTheftMultiplayer.Launcher.exe'
if (-not (Test-Path -LiteralPath $launcherAssembly)) {
    throw "Launcher not built: $launcherAssembly"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($launcherAssembly)
$asm = New-Object System.AppDomainSetup
Write-Host "Loaded launcher assembly."

# The updater takes its collaborators through the constructor, so build minimal
# fakes. Only the pieces the update path actually touches are needed.
$loggerType    = $assembly.GetType('GrandTheftMultiplayer.Launcher.Services.Runtime.Logger')
$notifType     = $assembly.GetType('GrandTheftMultiplayer.Launcher.Services.Runtime.NotificationService')
$splashType    = $assembly.GetType('GrandTheftMultiplayer.Launcher.Services.Runtime.SplashScreenProgressProvider')
$settingsType  = $assembly.GetType('GrandTheftMultiplayer.Launcher.Services.Runtime.SettingsProvider')
$updaterType   = $assembly.GetType('GrandTheftMultiplayer.Launcher.Services.Runtime.ClientFilesUpdater')

foreach ($t in @($loggerType, $notifType, $splashType, $settingsType, $updaterType)) {
    if (-not $t) { throw 'Could not resolve a launcher type.' }
}

function New-Updater {
    $logger = [Activator]::CreateInstance($loggerType)
    $notif  = New-Object PS.Object -Property @{ }
    $splash = [Activator]::CreateInstance($splashType)
    $settings = [Activator]::CreateInstance($settingsType)
    [Activator]::CreateInstance($updaterType, @($logger, $notif, $splash, $settings))
}

$updater = New-Updater
$method = $updaterType.GetMethod('UpdateClientFiles')
$result = $method.Invoke($updater, @())

Write-Host "UpdateClientFiles returned: $result"

$clientDll = Join-Path $LauncherDirectory 'bin\scripts\GrandTheftMultiplayer.Client.dll'
if (Test-Path -LiteralPath $clientDll) {
    $info = Get-Item -LiteralPath $clientDll
    Write-Host "OK  client present: $($info.FullName) ($($info.Length) bytes)"
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($clientDll).ProductVersion
    Write-Host "OK  version: $version"
} else {
    Write-Host "FAIL client missing at $clientDll"
    exit 1
}

$leftovers = Join-Path $LauncherDirectory 'tempstorage'
if (Test-Path -LiteralPath $leftovers) {
    Write-Host "WARN download folder still present: $leftovers"
} else {
    Write-Host "OK  download folder cleaned up"
}