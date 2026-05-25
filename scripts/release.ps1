[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("Archive", "Checksums", "Clean", "ToolSmoke", "ValidateVersion")]
    [string] $Action,

    [string] $Version = "0.0.0-dev",
    [string] $Runtime = "",
    [string] $ArtifactsDir = "artifacts",
    [string] $PublishDir = "artifacts/publish",
    [string] $DistDir = "artifacts/dist",
    [string] $PackageDir = "artifacts/packages",
    [string] $PackageId = "Nccurry.MtgMcp"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Normalize-Path {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::TrimEndingDirectorySeparator(
        [System.IO.Path]::GetFullPath($Path)
    )
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return Normalize-Path $Path
    }

    return Normalize-Path (Join-Path (Get-Location).ProviderPath $Path)
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

function New-CleanDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Resolve-RepoPath $Path
    Assert-SafeCleanPath $fullPath
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
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
    Invoke-Checked dotnet "tool" "install" $PackageId "--tool-path" $toolPath "--add-source" $packageSource "--version" $Version

    $toolExecutable = if ($IsWindows) {
        Join-Path $toolPath "mtg-mcp.exe"
    }
    else {
        Join-Path $toolPath "mtg-mcp"
    }

    if (-not (Test-Path -LiteralPath $toolExecutable)) {
        throw "Installed tool executable not found: $toolExecutable"
    }

    Invoke-Checked $toolExecutable "--smoke"
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
    "ToolSmoke" { Invoke-ToolSmoke }
    "ValidateVersion" { Assert-StableSemVer }
}
