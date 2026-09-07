[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$mise = Get-Command mise -ErrorAction SilentlyContinue

if ($null -eq $mise)
{
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget)
    {
        throw "Install mise from https://mise.jdx.dev/getting-started.html, then run this script again."
    }

    & $winget.Source install --id jdx.mise --exact --silent `
        --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $mise = Get-Command mise -ErrorAction SilentlyContinue
    if ($null -eq $mise)
    {
        throw "mise was installed. Open a new terminal, then run this script again."
    }
}

Push-Location $PSScriptRoot
try
{
    & $mise.Source trust --yes mise.toml
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $mise.Source install
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $mise.Source exec -- task setup
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Bootstrap complete. Run 'task <command>'; Task uses mise for .NET. If task is not on PATH, activate mise or use 'mise exec -- task <command>'."
}
finally
{
    Pop-Location
}
