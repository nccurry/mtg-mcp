[CmdletBinding()]
param(
    [string] $DotnetChannel = "11.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$globalJsonPath = Join-Path $repoRoot "global.json"
$headers = @{ "User-Agent" = "mtg-mcp-deps-update" }
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

# Keep the surrounding JSON layout stable while changing the single SDK pin.
$globalJsonText = Get-Content -LiteralPath $globalJsonPath -Raw
$updatedGlobalJson = [regex]::Replace(
    $globalJsonText,
    '("version"\s*:\s*")[^"]*(")',
    "`${1}$sdkVersion`${2}")
if ($updatedGlobalJson -eq $globalJsonText) {
    throw "Could not find sdk.version in $globalJsonPath."
}

Set-Content -LiteralPath $globalJsonPath -Value $updatedGlobalJson -NoNewline
Write-Host ".NET SDK (global.json) = $sdkVersion"
