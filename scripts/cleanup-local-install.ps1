param(
    [string] $InstallPath = "",
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path variable:IsWindows)) {
    $script:IsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Get-UserHome {
    if (-not [string]::IsNullOrWhiteSpace($HOME)) {
        return $HOME
    }

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        return $env:USERPROFILE
    }

    throw "Could not determine the current user's home directory."
}

function Get-CodexConfigPath {
    return Join-Path (Join-Path (Get-UserHome) ".codex") "config.toml"
}

function Expand-InstallPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $expandedPath = [System.Environment]::ExpandEnvironmentVariables($Path)
    if ($expandedPath.Equals("~", [System.StringComparison]::Ordinal)) {
        return Get-UserHome
    }

    if ($expandedPath.StartsWith("~/", [System.StringComparison]::Ordinal) `
        -or $expandedPath.StartsWith("~\", [System.StringComparison]::Ordinal)) {
        return Join-Path (Get-UserHome) $expandedPath.Substring(2)
    }

    return $expandedPath
}

function Get-ConfiguredMcpCommandPath {
    $configPath = Get-CodexConfigPath
    if (-not (Test-Path -LiteralPath $configPath)) {
        return ""
    }

    $inMtgMcpSection = $false
    foreach ($line in Get-Content -LiteralPath $configPath) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("[", [System.StringComparison]::Ordinal)) {
            $inMtgMcpSection = $trimmed.Equals(
                "[mcp_servers.mtg-mcp]",
                [System.StringComparison]::OrdinalIgnoreCase)
            continue
        }

        if (-not $inMtgMcpSection) {
            continue
        }

        if ($trimmed -match "^command\s*=\s*['""](?<command>.+?)['""]\s*$") {
            $commandPath = $Matches["command"]
            if ([System.IO.Path]::IsPathRooted($commandPath)) {
                return [System.IO.Path]::GetFullPath($commandPath)
            }
        }
    }

    return ""
}

function Get-InstallDirectory {
    param([string] $ConfiguredCommandPath)

    if (-not [string]::IsNullOrWhiteSpace($InstallPath)) {
        $expandedPath = Expand-InstallPath $InstallPath
        $fullPath = [System.IO.Path]::GetFullPath($expandedPath)
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            return $fullPath
        }

        return Split-Path -Parent $fullPath
    }

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredCommandPath)) {
        return Split-Path -Parent $ConfiguredCommandPath
    }

    return Join-Path (Join-Path (Get-UserHome) ".local") "bin"
}

function Remove-OldLocalBinary {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo] $File,
        [Parameter(Mandatory = $true)][string] $ConfiguredCommandPath
    )

    $isConfiguredCommand = -not [string]::IsNullOrWhiteSpace($ConfiguredCommandPath) `
        -and $File.FullName.Equals($ConfiguredCommandPath, [System.StringComparison]::OrdinalIgnoreCase)

    if ($isConfiguredCommand) {
        Write-Host "Keeping configured MCP command: $($File.FullName)"
        return "kept"
    }

    if ($DryRun) {
        Write-Host "Would remove old local binary: $($File.FullName)"
        return "dry-run"
    }

    try {
        Remove-Item -LiteralPath $File.FullName -Force
        Write-Host "Removed old local binary: $($File.FullName)"
        return "removed"
    }
    catch [System.IO.IOException] {
        Write-Host "Skipped locked local binary: $($File.FullName)"
        return "locked"
    }
    catch [System.UnauthorizedAccessException] {
        Write-Host "Skipped inaccessible local binary: $($File.FullName)"
        return "locked"
    }
}

$configuredCommandPath = Get-ConfiguredMcpCommandPath
$installDirectory = Get-InstallDirectory -ConfiguredCommandPath $configuredCommandPath
if (-not (Test-Path -LiteralPath $installDirectory -PathType Container)) {
    Write-Host "Local install directory does not exist: $installDirectory"
    exit 0
}

$pattern = if ($IsWindows) { "mtg-mcp-0.0.0-local.*.exe" } else { "mtg-mcp-0.0.0-local.*" }
$files = Get-ChildItem -LiteralPath $installDirectory -File -Filter $pattern

$removed = 0
$kept = 0
$locked = 0
$dryRunCount = 0
foreach ($file in $files) {
    $result = Remove-OldLocalBinary -File $file -ConfiguredCommandPath $configuredCommandPath
    switch ($result) {
        "removed" { $removed++ }
        "kept" { $kept++ }
        "locked" { $locked++ }
        "dry-run" { $dryRunCount++ }
    }
}

Write-Host "Cleanup summary: removed=$removed kept=$kept locked=$locked dryRun=$dryRunCount directory=$installDirectory"
