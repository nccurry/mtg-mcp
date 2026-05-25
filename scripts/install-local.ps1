param(
    [string] $Project = "src/MtgMcp.App/MtgMcp.App.csproj",
    [string] $Configuration = "Release",
    [string] $PackageId = "Nccurry.MtgMcp",
    [string] $PackageDir = "artifacts/packages",
    [string] $PublishDir = "artifacts/publish",
    [string] $Runtime = "win-x64",
    [string] $InstallPath = "",
    [string] $Version = ""
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

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function New-LocalVersion {
    $day = Get-Date -Format "yyyyMMdd"
    $time = Get-Date -Format "HHmmss"
    $shortSha = "nogit"
    try {
        $candidate = (& git rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) {
            $shortSha = "g$candidate"
        }
    }
    catch {
        $shortSha = "nogit"
    }

    return "0.0.0-local.$day.t$time.$shortSha"
}

function Get-ConfiguredMcpCommandPath {
    $configPath = Join-Path $env:USERPROFILE ".codex\config.toml"
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
                return $commandPath
            }
        }
    }

    return ""
}

function Set-ConfiguredMcpCommandPath {
    param([Parameter(Mandatory = $true)][string] $CommandPath)

    $configPath = Join-Path $env:USERPROFILE ".codex\config.toml"
    if (-not (Test-Path -LiteralPath $configPath)) {
        return $false
    }

    $lines = Get-Content -LiteralPath $configPath
    $inMtgMcpSection = $false
    $updated = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $trimmed = $lines[$index].Trim()
        if ($trimmed.StartsWith("[", [System.StringComparison]::Ordinal)) {
            $inMtgMcpSection = $trimmed.Equals(
                "[mcp_servers.mtg-mcp]",
                [System.StringComparison]::OrdinalIgnoreCase)
            continue
        }

        if ($inMtgMcpSection -and $trimmed -match "^command\s*=") {
            $escapedPath = $CommandPath.Replace("'", "''")
            $lines[$index] = "command = '$escapedPath'"
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        return $false
    }

    Set-Content -LiteralPath $configPath -Value $lines
    return $true
}

function Get-DefaultInstallPath {
    $configuredPath = Get-ConfiguredMcpCommandPath
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        return $configuredPath
    }

    $fileName = if ($IsWindows) { "mtg-mcp.exe" } else { "mtg-mcp" }
    return Join-Path $env:USERPROFILE ".local\bin\$fileName"
}

function Test-GlobalToolInstalled {
    $escapedPackageId = [System.Text.RegularExpressions.Regex]::Escape($PackageId)
    $installed = dotnet tool list --global
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list global dotnet tools."
    }

    return [bool]($installed | Select-String -Pattern "^\s*$escapedPackageId\s" -CaseSensitive:$false)
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = New-LocalVersion
}

$projectPath = Resolve-RepoPath $Project
$packageDirPath = Resolve-RepoPath $PackageDir
$publishRoot = Resolve-RepoPath $PublishDir
$publishOutput = Join-Path $publishRoot "local-install\$Runtime\$Version"

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    $InstallPath = Get-DefaultInstallPath
}

$installPathFull = [System.IO.Path]::GetFullPath(
    [System.Environment]::ExpandEnvironmentVariables($InstallPath)
)
$installDirectory = Split-Path -Parent $installPathFull

New-Item -ItemType Directory -Force -Path $packageDirPath | Out-Null
New-Item -ItemType Directory -Force -Path $publishOutput | Out-Null
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null

Write-Host "Packing $PackageId $Version"
Invoke-Checked dotnet `
    "pack" $projectPath `
    "--configuration" $Configuration `
    "--output" $packageDirPath `
    "-p:Version=$Version" `
    "-p:PackageVersion=$Version" `
    "-p:ContinuousIntegrationBuild=true"

if (Test-GlobalToolInstalled) {
    Write-Host "Updating global dotnet tool $PackageId to $Version"
    Invoke-Checked dotnet `
        "tool" "update" "--global" $PackageId `
        "--add-source" $packageDirPath `
        "--version" $Version
}
else {
    Write-Host "Installing global dotnet tool $PackageId $Version"
    Invoke-Checked dotnet `
        "tool" "install" "--global" $PackageId `
        "--add-source" $packageDirPath `
        "--version" $Version
}

Write-Host "Publishing self-contained $Runtime binary"
Invoke-Checked dotnet `
    "publish" $projectPath `
    "--configuration" $Configuration `
    "--runtime" $Runtime `
    "--self-contained" "true" `
    "--output" $publishOutput `
    "-p:Version=$Version" `
    "-p:PublishSingleFile=true" `
    "-p:PublishTrimmed=false"

$publishedName = if ($IsWindows) { "mtg-mcp.exe" } else { "mtg-mcp" }
$projectExecutableName = if ($IsWindows) { "MtgMcp.App.exe" } else { "MtgMcp.App" }
$publishedExecutable = Join-Path $publishOutput $publishedName
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    $publishedExecutable = Join-Path $publishOutput $projectExecutableName
}

if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable not found: $publishedExecutable"
}

$globalToolPath = if ($IsWindows) {
    Join-Path $env:USERPROFILE ".dotnet\tools\$publishedName"
}
else {
    Join-Path $HOME ".dotnet/tools/$publishedName"
}

$installedCommandPath = $installPathFull
try {
    Copy-Item -LiteralPath $publishedExecutable -Destination $installPathFull -Force
}
catch [System.IO.IOException] {
    $sideBySideName = if ($IsWindows) { "mtg-mcp-$Version.exe" } else { "mtg-mcp-$Version" }
    $sideBySidePath = Join-Path $installDirectory $sideBySideName
    Copy-Item -LiteralPath $publishedExecutable -Destination $sideBySidePath -Force
    if (-not (Set-ConfiguredMcpCommandPath -CommandPath $sideBySidePath)) {
        throw "Could not overwrite locked MCP command path '$installPathFull', and no Codex mtg-mcp command could be updated. New binary is at '$sideBySidePath'."
    }

    $installedCommandPath = $sideBySidePath
    Write-Host "Configured command was locked; updated Codex MCP config to $installedCommandPath"
}

Write-Host "Smoke-testing global tool shim"
if (Test-Path -LiteralPath $globalToolPath) {
    Invoke-Checked $globalToolPath "--smoke"
}
else {
    Write-Host "Global tool shim was not found at $globalToolPath"
}

Write-Host "Smoke-testing installed command path"
Invoke-Checked $installedCommandPath "--smoke"

Write-Host "Installed $PackageId $Version"
Write-Host "Global tool: $globalToolPath"
Write-Host "MCP command: $installedCommandPath"
