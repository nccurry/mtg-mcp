[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("Archive", "Checksums", "Clean", "LiveAcceptance", "ToolSmoke", "ValidateVersion")]
    [string] $Action,

    [string] $Version = "",
    [string] $Project = "src/MtgMcp.App/MtgMcp.App.csproj",
    [string] $E2ETestProject = "tests/MtgMcp.E2E.Tests/MtgMcp.E2E.Tests.csproj",
    [string] $Configuration = "Release",
    [string] $Runtime = "",
    [string] $ArtifactsDir = "artifacts",
    [string] $PublishDir = "artifacts/publish",
    [string] $DistDir = "artifacts/dist",
    [string] $PackageDir = "artifacts/packages",
    [string] $PackageId = "Nccurry.MtgMcp"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path variable:IsWindows)) {
    $script:IsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Normalize-Path {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPath = [System.IO.Path]::GetPathRoot($fullPath)
    while ($fullPath.Length -gt $rootPath.Length) {
        $lastCharacter = $fullPath[$fullPath.Length - 1]
        $isSeparator = $lastCharacter -eq [System.IO.Path]::DirectorySeparatorChar `
            -or $lastCharacter -eq [System.IO.Path]::AltDirectorySeparatorChar
        if (-not $isSeparator) {
            break
        }

        $fullPath = $fullPath.Substring(0, $fullPath.Length - 1)
    }

    return $fullPath
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return Normalize-Path $Path
    }

    return Normalize-Path (Join-Path (Get-Location).ProviderPath $Path)
}

function Get-DotnetCommand {
    $localName = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        "dotnet.exe"
    }
    else {
        "dotnet"
    }

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

    $env:DOTNET_ROOT = $localRoot

    if ($IsWindows) {
        $env:DOTNET_ROOT_X64 = $localRoot
    }
}

function Resolve-PackageVersion {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return $Version.Trim()
    }

    $dotnetCommand = Get-DotnetCommand
    $projectPath = Resolve-RepoPath $Project
    $output = & $dotnetCommand "msbuild" $projectPath "-nologo" "-getProperty:Version"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate the application package version."
    }

    $resolved = $output |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$' } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw "The application project did not provide a valid package version."
    }

    return $resolved
}

function Test-SameOrChildPath {
    param(
        [Parameter(Mandatory = $true)][string] $ParentPath,
        [Parameter(Mandatory = $true)][string] $CandidatePath
    )

    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    $parent = Normalize-Path $ParentPath
    $candidate = Normalize-Path $CandidatePath

    if ($candidate.Equals($parent, $comparison)) {
        return $true
    }

    return $candidate.StartsWith(
        "$parent$([System.IO.Path]::DirectorySeparatorChar)",
        $comparison
    )
}

function Assert-InRepoChild {
    param([Parameter(Mandatory = $true)][string] $Path)

    $repoRoot = Normalize-Path (Get-Location).ProviderPath
    $fullPath = Normalize-Path $Path
    if ($fullPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify the repository root: $fullPath"
    }

    if (-not (Test-SameOrChildPath -ParentPath $repoRoot -CandidatePath $fullPath)) {
        throw "Refusing to modify path outside the repository: $fullPath"
    }
}

function Assert-SafeCleanPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Resolve-RepoPath $Path
    $artifactsPath = Resolve-RepoPath $ArtifactsDir
    Assert-InRepoChild $artifactsPath
    Assert-InRepoChild $fullPath

    if (-not (Test-SameOrChildPath -ParentPath $artifactsPath -CandidatePath $fullPath)) {
        throw "Refusing to clean path outside the artifacts directory: $fullPath"
    }
}

function Assert-StableSemVer {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Release versions must use plain SemVer X.Y.Z, such as 0.1.0. Do not prefix tags with v."
    }
}

function Remove-DirectoryTree {
    param([Parameter(Mandatory = $true)][string] $Path)

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return
    }
    catch {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        if ([System.IO.Path]::DirectorySeparatorChar -ne '\') {
            throw
        }

        $extendedPath = if ($Path.StartsWith('\\', [System.StringComparison]::Ordinal)) {
            "\\?\UNC\$($Path.TrimStart('\'))"
        }
        else {
            "\\?\$Path"
        }
        [System.IO.Directory]::Delete($extendedPath, $true)
    }
}

function New-CleanDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Resolve-RepoPath $Path
    Assert-SafeCleanPath $fullPath
    if (Test-Path -LiteralPath $fullPath) {
        Remove-DirectoryTree $fullPath
    }

    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    return $fullPath
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Resolve-RepoPath $Path
    Assert-InRepoChild $fullPath
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    return $fullPath
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Rename-HostBinary {
    param([Parameter(Mandatory = $true)][string] $StagePath)

    $windowsHost = Join-Path $StagePath "MtgMcp.App.exe"
    $unixHost = Join-Path $StagePath "MtgMcp.App"

    if (Test-Path -LiteralPath $windowsHost) {
        Rename-Item -LiteralPath $windowsHost -NewName "mtg-mcp.exe" -Force
    }

    if (Test-Path -LiteralPath $unixHost) {
        Rename-Item -LiteralPath $unixHost -NewName "mtg-mcp" -Force
    }
}

function New-Archive {
    if ([string]::IsNullOrWhiteSpace($Runtime)) {
        throw "Runtime is required for Archive."
    }

    $publishRuntimeDir = Resolve-RepoPath (Join-Path $PublishDir $Runtime)
    if (-not (Test-Path -LiteralPath $publishRuntimeDir)) {
        throw "Publish output not found: $publishRuntimeDir"
    }

    $distPath = Ensure-Directory $DistDir
    $stageRoot = Ensure-Directory (Join-Path $ArtifactsDir "staging")
    $archiveName = "mtg-mcp-$Version-$Runtime"
    $stagePath = New-CleanDirectory (Join-Path $stageRoot $archiveName)

    Copy-Item -Path (Join-Path $publishRuntimeDir "*") -Destination $stagePath -Recurse -Force
    Rename-HostBinary $stagePath

    Copy-Item -LiteralPath (Resolve-RepoPath "README.md") -Destination $stagePath -Force
    Copy-Item -LiteralPath (Resolve-RepoPath "LICENSE") -Destination $stagePath -Force

    if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $archivePath = Join-Path $distPath "$archiveName.zip"
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        Compress-Archive -LiteralPath $stagePath -DestinationPath $archivePath -CompressionLevel Optimal
    }
    else {
        $archivePath = Join-Path $distPath "$archiveName.tar.gz"
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        $stageParent = Split-Path -Path $stagePath -Parent
        $stageLeaf = Split-Path -Path $stagePath -Leaf
        Invoke-Checked tar "-czf" $archivePath "-C" $stageParent $stageLeaf
    }

    Write-Host "Created $archivePath"
}

function New-Checksums {
    $distPath = Resolve-RepoPath $DistDir
    if (-not (Test-Path -LiteralPath $distPath)) {
        throw "Distribution directory not found: $distPath"
    }

    $files = Get-ChildItem -LiteralPath $distPath -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object Name

    if ($files.Count -eq 0) {
        throw "No distribution files found for checksums."
    }

    $lines = foreach ($file in $files) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Name)"
    }

    $checksumPath = Join-Path $distPath "SHA256SUMS.txt"
    [System.IO.File]::WriteAllLines($checksumPath, $lines)
    Write-Host "Created $checksumPath"
}

function Invoke-ToolSmoke {
    $packageSource = Resolve-RepoPath $PackageDir
    $packagePath = Join-Path $packageSource "$PackageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Package not found: $packagePath"
    }

    $toolPath = New-CleanDirectory (Join-Path $ArtifactsDir "tool-smoke")
    $dotnetCommand = Get-DotnetCommand
    Invoke-Checked $dotnetCommand "tool" "install" $PackageId "--tool-path" $toolPath "--add-source" $packageSource "--version" $Version

    $toolExecutable = if ($IsWindows) {
        Join-Path $toolPath "mtg-mcp.exe"
    }
    else {
        Join-Path $toolPath "mtg-mcp"
    }

    if (-not (Test-Path -LiteralPath $toolExecutable)) {
        throw "Installed tool executable not found: $toolExecutable"
    }

    Use-LocalDotnetRootForAppHosts
    Invoke-Checked $toolExecutable "--smoke"

    $previousCommand = $env:MTGMCP_E2E_COMMAND
    $previousVersion = $env:MTGMCP_E2E_VERSION
    try {
        $env:MTGMCP_E2E_COMMAND = $toolExecutable
        $env:MTGMCP_E2E_VERSION = $Version
        Invoke-Checked $dotnetCommand `
            "test" `
            (Resolve-RepoPath $E2ETestProject) `
            "--configuration" `
            $Configuration `
            "--no-build" `
            "--filter" `
            "FullyQualifiedName~FoundationMcpTests|FullyQualifiedName~DeckMcpTests|FullyQualifiedName~DeckInterchangeMcpTests|FullyQualifiedName~ToolsetNorthStarMcpTests|FullyQualifiedName~ScryfallMcpTests|FullyQualifiedName~StatisticsMcpTests"
    }
    finally {
        $env:MTGMCP_E2E_COMMAND = $previousCommand
        $env:MTGMCP_E2E_VERSION = $previousVersion
    }
}

function Invoke-LiveAcceptance {
    $workingTreeStatus = & git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the repository before live acceptance."
    }

    if ($workingTreeStatus) {
        throw "Live method acceptance requires a clean committed worktree."
    }

    $testedCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $testedCommit -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve the exact commit for live acceptance."
    }

    $packageSource = Resolve-RepoPath $PackageDir
    $packagePath = Join-Path $packageSource "$PackageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Package not found: $packagePath"
    }

    $toolDirectoryName = "la-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    $toolPath = New-CleanDirectory (Join-Path $ArtifactsDir $toolDirectoryName)
    $dotnetCommand = Get-DotnetCommand
    Invoke-Checked $dotnetCommand "tool" "install" $PackageId "--tool-path" $toolPath "--add-source" $packageSource "--version" $Version

    $toolExecutable = if ($IsWindows) {
        Join-Path $toolPath "mtg-mcp.exe"
    }
    else {
        Join-Path $toolPath "mtg-mcp"
    }

    if (-not (Test-Path -LiteralPath $toolExecutable)) {
        throw "Installed tool executable not found: $toolExecutable"
    }

    Use-LocalDotnetRootForAppHosts
    Invoke-Checked $toolExecutable "--smoke"

    $previousCommand = $env:MTGMCP_E2E_COMMAND
    $previousVersion = $env:MTGMCP_E2E_VERSION
    $previousCommit = $env:MTGMCP_LIVE_ACCEPTANCE_COMMIT
    try {
        $env:MTGMCP_E2E_COMMAND = $toolExecutable
        $env:MTGMCP_E2E_VERSION = $Version
        $env:MTGMCP_LIVE_ACCEPTANCE_COMMIT = $testedCommit
        Invoke-Checked $dotnetCommand `
            "test" `
            (Resolve-RepoPath $E2ETestProject) `
            "--configuration" `
            $Configuration `
            "--no-build" `
            "--filter" `
            "FullyQualifiedName~LiveMethodAcceptanceTests"
        $dataRoot = $env:MTGMCP_LIVE_ACCEPTANCE_DATA_DIR
        if ([string]::IsNullOrWhiteSpace($dataRoot)) {
            throw "MTGMCP_LIVE_ACCEPTANCE_DATA_DIR is required."
        }

        $verifyScript = Resolve-RepoPath "scripts/verify-live-method-acceptance.ps1"
        & $verifyScript -DataRoot $dataRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Live method journal verification failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:MTGMCP_E2E_COMMAND = $previousCommand
        $env:MTGMCP_E2E_VERSION = $previousVersion
        $env:MTGMCP_LIVE_ACCEPTANCE_COMMIT = $previousCommit
    }
}

if ($Action -in @("Archive", "LiveAcceptance", "ToolSmoke", "ValidateVersion")) {
    $Version = Resolve-PackageVersion
}

switch ($Action) {
    "Archive" { New-Archive }
    "Checksums" { New-Checksums }
    "Clean" {
        New-CleanDirectory $ArtifactsDir | Out-Null
        Ensure-Directory $PackageDir | Out-Null
        Ensure-Directory $DistDir | Out-Null
        Ensure-Directory $PublishDir | Out-Null
    }
    "LiveAcceptance" { Invoke-LiveAcceptance }
    "ToolSmoke" { Invoke-ToolSmoke }
    "ValidateVersion" { Assert-StableSemVer }
}
