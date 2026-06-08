[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $TaskVersion = "",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $TaskArgs
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($TaskArgs.Count -eq 0) {
    $TaskArgs = @("setup")
}

$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

function Get-VersionValue {
    param(
        [Parameter(Mandatory = $true)][string] $Name
    )

    $versionsPath = Join-Path $repoRoot "versions.env"
    if (-not (Test-Path -LiteralPath $versionsPath)) {
        throw "versions.env was not found at $versionsPath."
    }

    foreach ($line in Get-Content -LiteralPath $versionsPath) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed -split "=", 2
        if ($parts.Count -eq 2 -and $parts[0].Trim().Equals($Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $parts[1].Trim()
        }
    }

    throw "$Name is missing from versions.env."
}

if ([string]::IsNullOrWhiteSpace($TaskVersion)) {
    $TaskVersion = Get-VersionValue -Name "GO_TASK_VERSION"
}

function Get-TaskArchitecture {
    switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        "X64" { return "amd64" }
        "Arm64" { return "arm64" }
        default {
            throw "Unsupported processor architecture: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)"
        }
    }
}

function Test-TaskVersion {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    try {
        $actualVersion = (& $Path --version 2>$null).Trim()
        return $actualVersion -eq $TaskVersion
    }
    catch {
        return $false
    }
}

function Get-TaskCommand {
    $taskCommand = Get-Command task -ErrorAction SilentlyContinue
    if ($null -ne $taskCommand -and (Test-TaskVersion -Path $taskCommand.Source)) {
        return $taskCommand.Source
    }

    $architecture = Get-TaskArchitecture
    $taskDir = Join-Path $repoRoot ".tools\task\v$TaskVersion\windows-$architecture"
    $taskPath = Join-Path $taskDir "task.exe"
    if ((Test-Path -LiteralPath $taskPath) -and (Test-TaskVersion -Path $taskPath)) {
        return $taskPath
    }

    New-Item -ItemType Directory -Force -Path $taskDir | Out-Null

    $downloadUrl = "https://github.com/go-task/task/releases/download/v$TaskVersion/task_windows_$architecture.zip"
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "mtg-mcp-task-$TaskVersion-$([System.Guid]::NewGuid())"
    $archivePath = Join-Path $tempRoot "task.zip"

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    try {
        Write-Host "Downloading Task v$TaskVersion for Windows $architecture..."
        Invoke-WebRequest -Uri $downloadUrl -UseBasicParsing -OutFile $archivePath
        Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force

        $extractedTask = Get-ChildItem -LiteralPath $tempRoot -Filter task.exe -Recurse | Select-Object -First 1
        if ($null -eq $extractedTask) {
            throw "Task executable was not found in $downloadUrl."
        }

        Copy-Item -LiteralPath $extractedTask.FullName -Destination $taskPath -Force
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    return $taskPath
}

$task = Get-TaskCommand

Push-Location $repoRoot
try {
    & $task @TaskArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
