[CmdletBinding()]
param(
    [string] $Version,
    [string] $JsonFile,
    [string] $InstallDir = ".dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Version) -and [string]::IsNullOrWhiteSpace($JsonFile)) {
    throw "Provide -Version or -JsonFile."
}

$jsonPath = $null
$expectedVersion = $Version
if (-not [string]::IsNullOrWhiteSpace($JsonFile)) {
    $jsonPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $JsonFile))
    if (-not (Test-Path -LiteralPath $jsonPath)) {
        throw "global.json was not found at $jsonPath."
    }

    $globalJson = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    $expectedVersion = [string] $globalJson.sdk.version
}

$installPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $InstallDir))
$dotnetPath = Join-Path $installPath "dotnet.exe"

if ((-not [string]::IsNullOrWhiteSpace($expectedVersion)) -and (Test-Path -LiteralPath $dotnetPath)) {
    $installed = (& $dotnetPath --list-sdks 2>$null)
    if ($installed | Select-String -SimpleMatch "$expectedVersion ") {
        Write-Host ".NET SDK $expectedVersion is already installed under $installPath."
        exit 0
    }
}

New-Item -ItemType Directory -Force -Path $installPath | Out-Null
$scriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "mtg-mcp-dotnet-install.ps1"

try {
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -UseBasicParsing -OutFile $scriptPath
    if (-not [string]::IsNullOrWhiteSpace($jsonPath)) {
        & $scriptPath -JSonFile $jsonPath -InstallDir $installPath
    }
    else {
        & $scriptPath -Version $Version -InstallDir $installPath
    }
}
finally {
    Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
}

Write-Host ".NET SDK $expectedVersion installed under $installPath."
