[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $InstallDir = ".dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$installPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $InstallDir))
$dotnetPath = Join-Path $installPath "dotnet.exe"

if (Test-Path -LiteralPath $dotnetPath) {
    $actual = (& $dotnetPath --version 2>$null).Trim()
    if ($actual -eq $Version) {
        Write-Host ".NET SDK $actual is already installed under $installPath."
        exit 0
    }
}

New-Item -ItemType Directory -Force -Path $installPath | Out-Null
$scriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "mtg-mcp-dotnet-install-$Version.ps1"

try {
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -UseBasicParsing -OutFile $scriptPath
    & $scriptPath -Version $Version -InstallDir $installPath
}
finally {
    Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
}

Write-Host ".NET SDK $Version installed under $installPath."
