[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("VerifyGates")]
    [string] $Action,

    [string] $ReportPath = "artifacts/coverage/codecov.cobertura.xml",
    [double] $Threshold = 85,
    [string[]] $PackageName = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $Path))
}

function Invoke-VerifyGates {
    $fullReportPath = Resolve-RepoPath $ReportPath
    if (-not (Test-Path -LiteralPath $fullReportPath)) {
        throw "Coverage report not found: $fullReportPath"
    }

    [xml] $coverage = Get-Content -LiteralPath $fullReportPath
    $gates = @("MtgMcp.Core", "MtgMcp.Scryfall", "MtgMcp.Archidekt")

    if ($PackageName.Count -gt 0) {
        $requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $PackageName) {
            [void] $requested.Add($name)
        }

        $gates = @($gates | Where-Object { $requested.Contains($_) })
    }

    if ($gates.Count -eq 0) {
        throw "No coverage gates matched the requested package names."
    }

    $failed = $false
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $packages = @($coverage.SelectNodes("/coverage/packages/package"))

    foreach ($gate in $gates) {
        $package = @($packages | Where-Object { $_.GetAttribute("name") -eq $gate }) | Select-Object -First 1
        if ($null -eq $package) {
            throw "Coverage package not found in report: $gate"
        }

        $rate = [double]::Parse($package.GetAttribute("line-rate"), $culture) * 100.0
        $rateText = $rate.ToString("0.00", $culture)
        Write-Host "${gate}: $rateText% line coverage"

        if ($rate + 0.000001 -lt $Threshold) {
            Write-Error "$gate line coverage is below $Threshold%."
            $failed = $true
        }
    }

    if ($failed) {
        throw "Coverage gates failed."
    }
}

switch ($Action) {
    "VerifyGates" { Invoke-VerifyGates }
}
