[CmdletBinding()]
param(
    [string] $DotnetChannel = "11.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionsPath = Join-Path $repoRoot "versions.env"
$globalJsonPath = Join-Path $repoRoot "global.json"

$headers = @{ "User-Agent" = "mtg-mcp-deps-update" }
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    $headers["Authorization"] = "Bearer $($env:GITHUB_TOKEN)"
}

function Get-LatestGitHubVersion {
    param([Parameter(Mandatory = $true)][string] $Repository)

    # The /releases/latest endpoint already excludes prereleases and drafts.
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
    return ([string] $release.tag_name).TrimStart("v")
}

function Set-VersionsEnvValue {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Value
    )

    $lines = @(Get-Content -LiteralPath $versionsPath)
    $pattern = "^$([regex]::Escape($Name))="
    $updated = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) {
            $lines[$i] = "$Name=$Value"
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        throw "$Name was not found in $versionsPath."
    }

    Set-Content -LiteralPath $versionsPath -Value $lines
}

Write-Host "Querying latest tool and SDK versions..."

$taskVersion = Get-LatestGitHubVersion -Repository "go-task/task"
$powershellVersion = Get-LatestGitHubVersion -Repository "PowerShell/PowerShell"

$releasesIndex = Invoke-RestMethod -Headers $headers `
    -Uri "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

$channel = $null
foreach ($entry in $releasesIndex."releases-index") {
    if ($entry."channel-version" -eq $DotnetChannel) {
        $channel = $entry
        break
    }
}

if ($null -eq $channel) {
    throw "Channel $DotnetChannel was not found in the .NET releases index."
}

$sdkVersion = [string] $channel."latest-sdk"
if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw "The .NET releases index did not provide a latest-sdk for channel $DotnetChannel."
}

Set-VersionsEnvValue -Name "GO_TASK_VERSION" -Value $taskVersion
Set-VersionsEnvValue -Name "POWERSHELL_VERSION" -Value $powershellVersion

# global.json is the authoritative SDK pin; rewrite only the version value to preserve formatting.
$globalJsonText = Get-Content -LiteralPath $globalJsonPath -Raw
$updatedGlobalJson = [regex]::Replace(
    $globalJsonText,
    '("version"\s*:\s*")[^"]*(")',
    "`${1}$sdkVersion`${2}")
Set-Content -LiteralPath $globalJsonPath -Value $updatedGlobalJson -NoNewline

Write-Host "GO_TASK_VERSION    = $taskVersion"
Write-Host "POWERSHELL_VERSION = $powershellVersion"
Write-Host ".NET SDK (global.json) = $sdkVersion"
