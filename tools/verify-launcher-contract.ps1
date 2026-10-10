<#
.SYNOPSIS
    Verifies GTMP.Server.MasterApi against the launcher's real data models.

.DESCRIPTION
    The launcher deserializes every API response with Newtonsoft using default
    settings. A wrong field name, a camelCased property or a differently
    formatted date does not fail on the server side - it produces an empty
    object inside the launcher, where it looks like "no servers found".

    This script closes that gap. It loads the built launcher assembly and its
    Newtonsoft converters, fetches live responses from the master API and
    deserializes them into the launcher's own types.

    It must run under Windows PowerShell 5.1. PowerShell 7 runs on .NET and
    cannot load a .NET Framework WPF assembly.

.PARAMETER BaseUrl
    Root of a running GTMP.Server.MasterApi instance.

.PARAMETER LauncherBinDirectory
    Directory holding GrandTheftMultiplayer.Launcher.exe and Newtonsoft.Json.dll.

.EXAMPLE
    .\verify-launcher-contract.ps1 -BaseUrl http://127.0.0.1:4599 `
        -LauncherBinDirectory F:\GTMP\GTMP-Launcher\src\GTANetworkLauncher.WPF\bin\Debug
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $LauncherBinDirectory
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

$NewtonsoftPath = Join-Path $LauncherBinDirectory 'Newtonsoft.Json.dll'
$LauncherPath = Join-Path $LauncherBinDirectory 'GrandTheftMultiplayer.Launcher.exe'

foreach ($required in @($NewtonsoftPath, $LauncherPath)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Not found: $required. Build the launcher first."
    }
}

Add-Type -Path $NewtonsoftPath

# Add-Type refuses .exe, so the launcher assembly is loaded directly.
$null = [Reflection.Assembly]::LoadFrom($LauncherPath)

$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-Contract {
    param(
        [bool]   $Condition,
        [string] $Message
    )

    if ($Condition) {
        Write-Output "  OK   $Message"
    }
    else {
        Write-Output "  FAIL $Message"
        $script:Failures.Add($Message)
    }
}

function Get-Response {
    param([string] $Path)

    return (Invoke-WebRequest -Uri "$BaseUrl$Path" -UseBasicParsing -TimeoutSec 15).Content
}

function ConvertFrom-Response {
    param(
        [string] $Path,
        [type]   $TargetType
    )

    return [Newtonsoft.Json.JsonConvert]::DeserializeObject((Get-Response $Path), $TargetType)
}

Write-Output '== servers/detailed -> ServerApiResponse'
$list = ConvertFrom-Response '/servers/detailed' ([GrandTheftMultiplayer.Launcher.Models.ServerApi.ServerApiResponse])

Assert-Contract ($null -ne $list.List) 'List deserialized'
Assert-Contract ($list.List.Count -ge 1) "List has $($list.List.Count) entries"

$server = $list.List[0]
Assert-Contract (-not [string]::IsNullOrWhiteSpace($server.ServerName)) "ServerName = '$($server.ServerName)'"
Assert-Contract ($server.IpPortAddress -match '^\S+:\d+$') "ip = '$($server.IpPortAddress)' is host:port"
Assert-Contract ($server.Port -gt 0) "Port = $($server.Port)"
Assert-Contract (-not [string]::IsNullOrWhiteSpace($server.GameMode)) "GameMode = '$($server.GameMode)'"

Write-Output '== servers/verified -> VerifiedServersResponse'
$verified = ConvertFrom-Response '/servers/verified' ([GrandTheftMultiplayer.Launcher.Models.VerifiedServers.VerifiedServersResponse])
Assert-Contract ($verified.List -is [string[]]) 'List is a string array'

Write-Output '== stats/masterlist -> StatisticsResponse'
$stats = ConvertFrom-Response '/stats/masterlist' ([GrandTheftMultiplayer.Launcher.Models.Statistics.StatisticsResponse])
Assert-Contract ($stats.TotalServersOnline -ge 0) "TotalServersOnline = $($stats.TotalServersOnline)"
Assert-Contract ($stats.TotalVerifiedServersOnline -ge 0) "TotalVerifiedServersOnline = $($stats.TotalVerifiedServersOnline)"

Write-Output '== api/v1/components -> ServerStatusApiResponse'
$status = ConvertFrom-Response '/api/v1/components' ([GrandTheftMultiplayer.Launcher.Models.Status.ServerStatusApiResponse])
Assert-Contract ($null -ne $status.Meta) 'Meta deserialized'

# "Oagination" is the launcher's misspelling and is therefore the contract.
Assert-Contract ($null -ne $status.Meta.Oagination) 'Meta.Oagination deserialized (launcher spelling)'

$component = $status.Data[0]
Assert-Contract (-not [string]::IsNullOrWhiteSpace($component.Name)) "Name = '$($component.Name)'"
Assert-Contract ($component.Status -is [GrandTheftMultiplayer.Launcher.Models.Status.ServerStatuses]) "Status = $($component.Status)"
Assert-Contract ($component.CreatedAt.Year -gt 2000) "create_at parsed as $($component.CreatedAt)"

Write-Output '== /?news -> List[ForumPostItem]'
$news = [Newtonsoft.Json.JsonConvert]::DeserializeObject(
    (Get-Response '/?news'),
    [System.Collections.Generic.List[GrandTheftMultiplayer.Launcher.Models.Forum.ForumPostItem]])

foreach ($post in $news) {
    Assert-Contract ($post.Id -ne 0) "thread_id = $($post.Id)"
    Assert-Contract (-not [string]::IsNullOrWhiteSpace($post.Title)) "Title = '$($post.Title)'"
    Assert-Contract (-not [string]::IsNullOrWhiteSpace($post.Author)) "username = '$($post.Author)'"
    Assert-Contract ($post.Timestamp.Year -gt 2000) "post_date parsed as $($post.Timestamp)"
}

Write-Output ''

if ($script:Failures.Count -eq 0) {
    Write-Output 'RESULT: all launcher contract checks passed'
    exit 0
}

Write-Output "RESULT: $($script:Failures.Count) check(s) FAILED"
exit 1
