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

function Test-ExcludedFile {
    param(
        [Parameter(Mandatory = $true)][string] $Filename,
        [Parameter(Mandatory = $true)][string[]] $Patterns
    )

    foreach ($pattern in $Patterns) {
        if ($Filename -match $pattern) {
            return $true
        }
    }

    return $false
}

function Get-LineCoverage {
    param(
        [Parameter(Mandatory = $true)][object] $Package,
        [Parameter(Mandatory = $true)][string[]] $Exclude
    )

    $covered = 0
    $valid = 0

    foreach ($class in @($Package.classes.class)) {
        $filename = [string] $class.filename
        if (Test-ExcludedFile -Filename $filename -Patterns $Exclude) {
            continue
        }

        foreach ($line in @($class.lines.line)) {
            $valid++
            if ([int] $line.hits -gt 0) {
                $covered++
            }
        }
    }

    return [pscustomobject]@{
        Covered = $covered
        Valid = $valid
        Rate = if ($valid -eq 0) { 0.0 } else { ($covered / $valid) * 100.0 }
    }
}

function Invoke-VerifyGates {
    $fullReportPath = Resolve-RepoPath $ReportPath
    if (-not (Test-Path -LiteralPath $fullReportPath)) {
        throw "Coverage report not found: $fullReportPath"
    }

    [xml] $coverage = Get-Content -LiteralPath $fullReportPath
    $gates = @(
        @{
            Name = "MtgMcp.Core"
            Exclude = @("[\\/]obj[\\/]", "[\\/]Models\.cs$", "[\\/]Options\.cs$")
        },
        @{
            Name = "MtgMcp.Scryfall"
            Exclude = @("[\\/]obj[\\/]", "[\\/]ScryfallOptions\.cs$", "[\\/]ScryfallServiceCollectionExtensions\.cs$")
        },
        @{
            Name = "MtgMcp.Archidekt"
            Exclude = @("[\\/]obj[\\/]", "[\\/]ArchidektOptions\.cs$", "[\\/]ArchidektServiceCollectionExtensions\.cs$")
        }
    )

    if ($PackageName.Count -gt 0) {
        $requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $PackageName) {
            [void] $requested.Add($name)
        }

        $gates = @($gates | Where-Object { $requested.Contains($_.Name) })
    }

    if ($gates.Count -eq 0) {
        throw "No coverage gates matched the requested package names."
    }

    $failed = $false
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    foreach ($gate in $gates) {
        $package = @($coverage.coverage.packages.package | Where-Object { $_.name -eq $gate.Name }) | Select-Object -First 1
        if ($null -eq $package) {
            throw "Coverage package not found in report: $($gate.Name)"
        }

        $result = Get-LineCoverage -Package $package -Exclude $gate.Exclude
        if ($result.Valid -eq 0) {
            throw "Coverage package has no included lines after exclusions: $($gate.Name)"
        }

        $rateText = $result.Rate.ToString("0.00", $culture)
        Write-Host "$($gate.Name): $rateText% line coverage ($($result.Covered)/$($result.Valid))"

        if ($result.Rate + 0.000001 -lt $Threshold) {
            Write-Error "$($gate.Name) line coverage is below $Threshold%."
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
