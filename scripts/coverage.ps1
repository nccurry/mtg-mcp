[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("VerifyGates")]
    [string] $Action,

    [string] $ReportPath = "artifacts/coverage/codecov.cobertura.xml",
    [double] $Threshold = 90,
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
    $gates = @(
        "MtgMcp.App",
        "MtgMcp.Archidekt",
        "MtgMcp.Core",
        "MtgMcp.Decks",
        "MtgMcp.Scryfall"
    )

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
    $packages = @($coverage.SelectNodes("/*[local-name()='coverage']/*[local-name()='packages']/*[local-name()='package']"))
    $classes = @($coverage.SelectNodes("//*[local-name()='class']"))

    foreach ($gate in $gates) {
        $package = @($packages | Where-Object { Test-PackageMatchesGate -PackageNode $_ -Gate $gate }) | Select-Object -First 1
        $metrics = Get-ClassCoverageMetrics -Classes $classes -Gate $gate
        if ($null -ne $package) {
            $rate = [double]::Parse($package.GetAttribute("line-rate"), $culture) * 100.0
            $branchRate = [double]::Parse($package.GetAttribute("branch-rate"), $culture) * 100.0
        }
        else {
            $rate = $metrics.LineRate
            $branchRate = $metrics.BranchRate
        }

        $rateText = $rate.ToString("0.00", $culture)
        $branchText = $branchRate.ToString("0.00", $culture)
        $methodText = $metrics.MethodRate.ToString("0.00", $culture)
        Write-Host "${gate}: line $rateText%; branch $branchText%; method $methodText%"

        if ($rate + 0.000001 -lt $Threshold) {
            Write-Error "$gate line coverage is below $Threshold%."
            $failed = $true
        }
    }

    if ($failed) {
        throw "Coverage gates failed."
    }
}

function Get-ClassCoverageMetrics {
    param(
        [object[]] $Classes,
        [Parameter(Mandatory = $true)] [string] $Gate
    )

    $matchedClasses = @($Classes | Where-Object { Test-ClassMatchesGate -ClassNode $_ -Gate $Gate })
    if ($matchedClasses.Count -eq 0) {
        $sourceRoot = Resolve-RepoPath "src/$Gate"
        $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter "*.cs" -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        if ($sourceFiles.Count -eq 0) {
            return [pscustomobject]@{
                LineRate = 100.0
                BranchRate = 100.0
                MethodRate = 100.0
            }
        }

        throw "Coverage package not found in report for $Gate, which has $($sourceFiles.Count) source files."
    }

    $coveredLines = 0
    $coverableLines = 0
    $coveredBranches = 0
    $coverableBranches = 0
    $coveredMethods = 0
    $coverableMethods = 0
    foreach ($class in $matchedClasses) {
        foreach ($line in @($class.SelectNodes("*[local-name()='lines']/*[local-name()='line']"))) {
            $coverableLines++
            if ([int]::Parse($line.GetAttribute("hits"), [System.Globalization.CultureInfo]::InvariantCulture) -gt 0) {
                $coveredLines++
            }

            $conditionCoverage = $line.GetAttribute("condition-coverage")
            if ($conditionCoverage -match '\((\d+)/(\d+)\)') {
                $coveredBranches += [int]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
                $coverableBranches += [int]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }

        foreach ($method in @($class.SelectNodes("*[local-name()='methods']/*[local-name()='method']"))) {
            $coverableMethods++
            $methodLines = @($method.SelectNodes("*[local-name()='lines']/*[local-name()='line']"))
            if ($methodLines | Where-Object {
                    [int]::Parse($_.GetAttribute("hits"), [System.Globalization.CultureInfo]::InvariantCulture) -gt 0
                } | Select-Object -First 1) {
                $coveredMethods++
            }
        }
    }

    if ($coverableLines -eq 0) {
        throw "Coverage package has no coverable lines in report: $Gate"
    }

    return [pscustomobject]@{
        LineRate = ($coveredLines / $coverableLines) * 100.0
        BranchRate = if ($coverableBranches -eq 0) { 100.0 } else { ($coveredBranches / $coverableBranches) * 100.0 }
        MethodRate = if ($coverableMethods -eq 0) { 100.0 } else { ($coveredMethods / $coverableMethods) * 100.0 }
    }
}

function Test-PackageMatchesGate {
    param(
        [Parameter(Mandatory = $true)] [System.Xml.XmlElement] $PackageNode,
        [Parameter(Mandatory = $true)] [string] $Gate
    )

    $packageName = $PackageNode.GetAttribute("name").Replace("\", "/")
    return $packageName -eq $Gate -or
        $packageName.EndsWith(".$Gate", [System.StringComparison]::OrdinalIgnoreCase) -or
        $packageName.IndexOf("/$Gate/", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $packageName.IndexOf($Gate, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
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
