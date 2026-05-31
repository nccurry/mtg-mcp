param(
    [string] $Project = "src/MtgMcp.App/MtgMcp.App.csproj",
    [string] $Configuration = "Release",
    [string] $PackageId = "Nccurry.MtgMcp",
    [string] $PackageDir = "artifacts/packages",
    [string] $PublishDir = "artifacts/publish",
    [string] $Runtime = "",
    [string] $InstallPath = "",
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path variable:IsWindows)) {
    $script:IsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

if (-not (Test-Path variable:IsMacOS)) {
    $script:IsMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX)
}

if (-not (Test-Path variable:IsLinux)) {
    $script:IsLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux)
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $Path))
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

function Get-DotnetCliHome {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) {
        return $env:DOTNET_CLI_HOME
    }

    return Get-UserHome
}

function Get-DotnetCommand {
    $localName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
    $localPath = Join-Path (Join-Path (Get-Location).ProviderPath ".dotnet") $localName
    if (Test-Path -LiteralPath $localPath) {
        return $localPath
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "Could not find dotnet. Run task setup or install the .NET SDK listed in global.json."
}

function Use-LocalDotnetRootForAppHosts {
    $localName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
    $localRoot = Join-Path (Get-Location).ProviderPath ".dotnet"
    $localDotnet = Join-Path $localRoot $localName
    if (-not (Test-Path -LiteralPath $localDotnet)) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $env:DOTNET_ROOT = $localRoot
    }

    if ($IsWindows -and [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT_X64)) {
        $env:DOTNET_ROOT_X64 = $localRoot
    }
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

function Get-DefaultRuntime {
    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        "X64" { "x64" }
        "Arm64" { "arm64" }
        default { throw "Unsupported OS architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)" }
    }

    if ($IsWindows) {
        return "win-$architecture"
    }

    if ($IsMacOS) {
        return "osx-$architecture"
    }

    if ($IsLinux) {
        return "linux-$architecture"
    }

    throw "Unsupported OS platform."
}

function Set-ExecutableBit {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ($IsWindows) {
        return
    }

    Invoke-Checked chmod "+x" $Path
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

function Get-LocalVersionBase {
    $serverJsonPath = Resolve-RepoPath "server.json"
    if (-not (Test-Path -LiteralPath $serverJsonPath)) {
        return "0.0.0"
    }

    try {
        $serverJson = Get-Content -LiteralPath $serverJsonPath -Raw | ConvertFrom-Json
        foreach ($package in @($serverJson.packages)) {
            $matchesPackage = $package.identifier -eq $PackageId
            $hasVersion = -not [string]::IsNullOrWhiteSpace($package.version)
            if ($matchesPackage -and $hasVersion) {
                return [string] $package.version
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($serverJson.version)) {
            return [string] $serverJson.version
        }
    }
    catch {
        Write-Host "Could not read server.json version; falling back to 0.0.0."
    }

    return "0.0.0"
}

function New-LocalVersion {
    param([Parameter(Mandatory = $true)][string] $BaseVersion)

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

    $versionCore = $BaseVersion
    $buildMetadataIndex = $versionCore.IndexOf('+')
    if ($buildMetadataIndex -ge 0) {
        $versionCore = $versionCore.Substring(0, $buildMetadataIndex)
    }

    if ($versionCore.Contains("-")) {
        return "$versionCore.local.$day.t$time.$shortSha"
    }

    $stableMatch = [regex]::Match($versionCore, "^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$")
    if ($stableMatch.Success) {
        $major = $stableMatch.Groups["major"].Value
        $minor = $stableMatch.Groups["minor"].Value
        $patch = [int] $stableMatch.Groups["patch"].Value + 1
        return "$major.$minor.$patch-local.$day.t$time.$shortSha"
    }

    return "$versionCore-local.$day.t$time.$shortSha"
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
                return $commandPath
            }
        }
    }

    return ""
}

function Set-ConfiguredMcpCommandPath {
    param([Parameter(Mandatory = $true)][string] $CommandPath)

    $configPath = Get-CodexConfigPath
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
    $installDirectory = Join-Path (Join-Path (Get-UserHome) ".local") "bin"
    return Join-Path $installDirectory $fileName
}

function Test-GlobalToolInstalled {
    $escapedPackageId = [System.Text.RegularExpressions.Regex]::Escape($PackageId)
    $installed = & $DotnetCommand tool list --global
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list global dotnet tools."
    }

    return [bool]($installed | Select-String -Pattern "^\s*$escapedPackageId\s" -CaseSensitive:$false)
}

function Install-GlobalToolPackage {
    if (Test-GlobalToolInstalled) {
        Write-Host "Updating global dotnet tool $PackageId to $Version"
        & $DotnetCommand `
            "tool" "update" "--global" $PackageId `
            "--add-source" $packageDirPath `
            "--version" $Version
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Write-Host "Update failed; reinstalling global dotnet tool $PackageId $Version"
        Invoke-Checked $DotnetCommand "tool" "uninstall" "--global" $PackageId
    }
    else {
        Write-Host "Installing global dotnet tool $PackageId $Version"
    }

    Invoke-Checked $DotnetCommand `
        "tool" "install" "--global" $PackageId `
        "--add-source" $packageDirPath `
        "--version" $Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = New-LocalVersion -BaseVersion (Get-LocalVersionBase)
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = Get-DefaultRuntime
}

$projectPath = Resolve-RepoPath $Project
$packageDirPath = Resolve-RepoPath $PackageDir
$publishRoot = Resolve-RepoPath $PublishDir
$publishOutput = Join-Path $publishRoot "local-install\$Runtime\$Version"
$DotnetCommand = Get-DotnetCommand

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    $InstallPath = Get-DefaultInstallPath
}

$installPathFull = [System.IO.Path]::GetFullPath((Expand-InstallPath $InstallPath))
$installDirectory = Split-Path -Parent $installPathFull

New-Item -ItemType Directory -Force -Path $packageDirPath | Out-Null
New-Item -ItemType Directory -Force -Path $publishOutput | Out-Null
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null

Write-Host "Packing $PackageId $Version"
Invoke-Checked $DotnetCommand `
    "pack" $projectPath `
    "--configuration" $Configuration `
    "--output" $packageDirPath `
    "-p:Version=$Version" `
    "-p:PackageVersion=$Version" `
    "-p:ContinuousIntegrationBuild=true"

Install-GlobalToolPackage

Write-Host "Publishing self-contained $Runtime binary"
Invoke-Checked $DotnetCommand `
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

$globalToolDirectory = Join-Path (Join-Path (Get-DotnetCliHome) ".dotnet") "tools"
$globalToolPath = Join-Path $globalToolDirectory $publishedName

$installedCommandPath = $installPathFull
try {
    Copy-Item -LiteralPath $publishedExecutable -Destination $installPathFull -Force
    Set-ExecutableBit $installPathFull
}
catch [System.IO.IOException] {
    $sideBySideName = if ($IsWindows) { "mtg-mcp-$Version.exe" } else { "mtg-mcp-$Version" }
    $sideBySidePath = Join-Path $installDirectory $sideBySideName
    Copy-Item -LiteralPath $publishedExecutable -Destination $sideBySidePath -Force
    Set-ExecutableBit $sideBySidePath
    if (-not (Set-ConfiguredMcpCommandPath -CommandPath $sideBySidePath)) {
        throw "Could not overwrite locked MCP command path '$installPathFull', and no Codex mtg-mcp command could be updated. New binary is at '$sideBySidePath'."
    }

    $installedCommandPath = $sideBySidePath
    Write-Host "Configured command was locked; updated Codex MCP config to $installedCommandPath"
}

Write-Host "Smoke-testing global tool shim"
Use-LocalDotnetRootForAppHosts
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
