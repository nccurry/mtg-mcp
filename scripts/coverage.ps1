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
    $classes = @($coverage.SelectNodes("//class"))

    foreach ($gate in $gates) {
        $package = @($packages | Where-Object { $_.GetAttribute("name") -eq $gate }) | Select-Object -First 1
        if ($null -ne $package) {
            $rate = [double]::Parse($package.GetAttribute("line-rate"), $culture) * 100.0
        }
        else {
            $rate = Get-ClassCoverageRate -Classes $classes -Gate $gate
        }

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

function Get-ClassCoverageRate {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Classes,
        [Parameter(Mandatory = $true)] [string] $Gate
    )

    $matchedClasses = @($Classes | Where-Object { Test-ClassMatchesGate -ClassNode $_ -Gate $Gate })
    if ($matchedClasses.Count -eq 0) {
        throw "Coverage package not found in report: $Gate"
    }

    $coveredLines = 0
    $coverableLines = 0
    foreach ($class in $matchedClasses) {
        foreach ($line in @($class.SelectNodes("lines/line"))) {
            $coverableLines++
            if ([int]::Parse($line.GetAttribute("hits"), [System.Globalization.CultureInfo]::InvariantCulture) -gt 0) {
                $coveredLines++
            }
        }
    }

    if ($coverableLines -eq 0) {
        throw "Coverage package has no coverable lines in report: $Gate"
    }

    return ($coveredLines / $coverableLines) * 100.0
}

function Test-ClassMatchesGate {
    param(
        [Parameter(Mandatory = $true)] [System.Xml.XmlElement] $ClassNode,
        [Parameter(Mandatory = $true)] [string] $Gate
    )

    $className = $ClassNode.GetAttribute("name")
    if ($className -eq $Gate -or $className.StartsWith("$Gate.", [System.StringComparison]::Ordinal)) {
        return $true
    }

    $filename = $ClassNode.GetAttribute("filename").Replace("\", "/")
    $relativePrefix = "src/$Gate/"
    $pathSegment = "/$relativePrefix"
    return $filename.StartsWith($relativePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $filename.IndexOf($pathSegment, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

switch ($Action) {
    "VerifyGates" { Invoke-VerifyGates }
}
