[CmdletBinding()]
param(
    [switch] $Check
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$globalJsonPath = Join-Path $repoRoot "global.json"
$packagesPropsPath = Join-Path $repoRoot "Directory.Packages.props"
$readmePath = Join-Path $repoRoot "README.md"

$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$sdkVersion = [string] $globalJson.sdk.version

# The shared runtime/package band tracks the SDK. Preview SDKs shaped like
# MAJOR.MINOR.FEATUREBAND-preview.N.BUILD map to runtime MAJOR.MINOR.0-preview.N.BUILD,
# which is the version the Microsoft.Extensions.* packages ship under.
$sdkMatch = [regex]::Match($sdkVersion, '^(?<major>\d+)\.(?<minor>\d+)\.\d+(?<suffix>-.+)$')
if (-not $sdkMatch.Success) {
    throw "SDK version '$sdkVersion' in global.json is not a recognized preview version. Update Microsoft.Extensions.* manually or extend sync-versions.ps1 for stable releases."
}

$major = $sdkMatch.Groups["major"].Value
$minor = $sdkMatch.Groups["minor"].Value
$suffix = $sdkMatch.Groups["suffix"].Value
$extensionsVersion = "$major.$minor.0$suffix"
$dotnetBadgeVersion = "$major.$minor"

$changes = New-Object System.Collections.Generic.List[string]

$packagesText = Get-Content -LiteralPath $packagesPropsPath -Raw
$updatedPackagesText = [regex]::Replace(
    $packagesText,
    '(Include="Microsoft\.Extensions\.[^"]*"\s+Version=")[^"]*(")',
    "`${1}$extensionsVersion`${2}")
if ($updatedPackagesText -ne $packagesText) {
    $changes.Add("Directory.Packages.props Microsoft.Extensions.* -> $extensionsVersion")
}

$readmeText = Get-Content -LiteralPath $readmePath -Raw
$updatedReadmeText = [regex]::Replace(
    $readmeText,
    '(badge/\.NET-)[^-]*(-512BD4)',
    "`${1}$dotnetBadgeVersion`${2}")
if ($updatedReadmeText -ne $readmeText) {
    $changes.Add("README.md .NET badge -> $dotnetBadgeVersion")
}

if ($Check) {
    if ($changes.Count -gt 0) {
        Write-Host "Derived version references are out of sync with global.json (SDK $sdkVersion):"
        foreach ($change in $changes) {
            Write-Host "  $change"
        }

        throw "Run task deps:sync to update derived version references."
    }

    Write-Host "Derived version references are in sync with global.json (SDK $sdkVersion)."
    return
}

if ($updatedPackagesText -ne $packagesText) {
    Set-Content -LiteralPath $packagesPropsPath -Value $updatedPackagesText -NoNewline
}

if ($updatedReadmeText -ne $readmeText) {
    Set-Content -LiteralPath $readmePath -Value $updatedReadmeText -NoNewline
}

if ($changes.Count -eq 0) {
    Write-Host "Derived version references already in sync with global.json (SDK $sdkVersion)."
}
else {
    Write-Host "Synced derived version references from global.json (SDK $sdkVersion):"
    foreach ($change in $changes) {
        Write-Host "  $change"
    }
}
