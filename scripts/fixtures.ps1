[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("Refresh")]
    [string] $Action,

    [string] $TestsRoot = "tests",
    [int] $PaceMilliseconds = 750
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Fixture refresh is a manual, read-only provider call. It is never part of test or CI.
$userAgent = "mtg-mcp-fixtures/1.0 (+https://github.com/nccurry/mtg-mcp)"

. (Join-Path $PSScriptRoot "fixtures.functions.ps1")

function Get-Sha256 {
    param([string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8 {
    param([string] $Path, [string] $Text)

    [System.IO.File]::WriteAllBytes($Path, [System.Text.UTF8Encoding]::new($false).GetBytes($Text))
}

# Reads the adapter's credentials file (JSON or username=/password= lines) into a username and password pair.
function Read-ArchidektCredentials {
    $path = $env:MTGMCP__ARCHIDEKT__CREDENTIALS_FILE
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".mtg-mcp/archidekt.json"
    }

    if (-not (Test-Path -LiteralPath $path)) {
        throw "Archidekt credentials file not found; set MTGMCP__ARCHIDEKT__CREDENTIALS_FILE or create ~/.mtg-mcp/archidekt.json."
    }

    $text = Get-Content -LiteralPath $path -Raw
    $values = @{}
    if ($text.TrimStart().StartsWith("{")) {
        $json = $text | ConvertFrom-Json
        foreach ($property in $json.PSObject.Properties) {
            $values[$property.Name.ToLowerInvariant()] = [string] $property.Value
        }
    }
    else {
        foreach ($line in ($text -split "`r?`n")) {
            $index = $line.IndexOf("=")
            if ($index -gt 0 -and -not $line.TrimStart().StartsWith("#")) {
                $values[$line.Substring(0, $index).Trim().ToLowerInvariant()] = $line.Substring($index + 1).Trim()
            }
        }
    }

    if (-not $values["username"] -or -not $values["password"]) {
        throw "Archidekt credentials file lacks a username or password."
    }

    return @{ Username = $values["username"]; Password = $values["password"] }
}

# Logs in once the way the adapter does. The response is read only for its token and is never recorded.
function Get-ArchidektAuth {
    $credentials = Read-ArchidektCredentials
    $field = if ($credentials.Username.Contains("@")) { "email" } else { "username" }
    $payload = @{ $field = $credentials.Username; password = $credentials.Password } | ConvertTo-Json -Compress
    Assert-ReadOnlyRequest -Method "POST" -Path "/api/rest-auth/login/"
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "https://archidekt.com/api/rest-auth/login/")
    $request.Headers.TryAddWithoutValidation("User-Agent", $userAgent) | Out-Null
    $request.Headers.TryAddWithoutValidation("Accept", "application/json") | Out-Null
    $request.Content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, "application/json")
    $client = [System.Net.Http.HttpClient]::new()
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "Archidekt login failed with status $([int] $response.StatusCode)."
        }

        $root = $body | ConvertFrom-Json
        foreach ($name in @("token", "access_token", "access", "jwt", "key")) {
            if ($root.PSObject.Properties.Name -contains $name -and $root.$name) {
                return @{ Token = [string] $root.$name; Username = $credentials.Username }
            }
        }

        throw "Archidekt login returned no recognized token field."
    }
    finally {
        $client.Dispose()
    }
}

function Invoke-ProviderRead {
    param($Entry, $Auth)

    # The manifest keeps a {username} placeholder so the owner's name never reaches a recorded URL.
    $url = $Entry.url
    if ($null -ne $Auth) {
        $url = $url.Replace("{username}", [System.Uri]::EscapeDataString($Auth.Username))
    }

    $uri = [System.Uri] $url
    $method = $Entry.method.ToUpperInvariant()
    Assert-ReadOnlyRequest -Method $method -Path $uri.AbsolutePath

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), $uri)
    $request.Headers.TryAddWithoutValidation("User-Agent", $userAgent) | Out-Null
    $request.Headers.TryAddWithoutValidation("Accept", "application/json") | Out-Null
    if ($null -ne $Auth) {
        $request.Headers.TryAddWithoutValidation("Authorization", "JWT $($Auth.Token)") | Out-Null
    }

    if ($method -eq "POST") {
        $request.Content = [System.Net.Http.StringContent]::new($Entry.requestBody, [System.Text.Encoding]::UTF8, "application/json")
    }

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([int] $response.StatusCode -ne [int] $Entry.status) {
            throw "Entry $($Entry.id) expected status $($Entry.status) but received $([int] $response.StatusCode)."
        }

        return $body
    }
    finally {
        $client.Dispose()
    }
}

function Invoke-Refresh {
    $manifestPaths = Get-ChildItem -Path $TestsRoot -Recurse -Filter "manifest.json" |
        Where-Object { $_.Directory.Name -eq "recorded" } |
        Sort-Object FullName
    $first = $true
    $auth = $null
    foreach ($manifestPath in $manifestPaths) {
        $manifest = Get-Content -LiteralPath $manifestPath.FullName -Raw | ConvertFrom-Json -DateKind String
        $staging = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $staging | Out-Null
        try {
            # Recordings are staged and moved into place only after every entry of this manifest succeeds.
            $staged = @()
            foreach ($entry in $manifest.entries) {
                $path = Join-Path $manifestPath.Directory.FullName $entry.file
                if ($entry.refresh) {
                    if (-not $first) {
                        Start-Sleep -Milliseconds $PaceMilliseconds
                    }

                    $first = $false
                    $entryAuth = $null
                    if ((Test-Member $entry "auth") -and $entry.auth) {
                        if ($null -eq $auth) {
                            $auth = Get-ArchidektAuth
                        }

                        $entryAuth = $auth
                    }

                    $text = ConvertTo-StableJson -Text (Invoke-ProviderRead -Entry $entry -Auth $entryAuth) -Entry $entry
                    $temp = Join-Path $staging "$($staged.Count).json"
                    Write-Utf8 -Path $temp -Text $text
                    $staged += , @($temp, $path)
                    $after = Get-Sha256 -Path $temp
                }
                else {
                    $after = Get-Sha256 -Path $path
                }

                # The manifest hash, not the file on disk, says whether the recording moved.
                $changed = $after -ne $entry.sha256
                if ($changed) {
                    $entry.capturedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
                }

                $entry.sha256 = $after
                Write-Host "$($entry.id): $(if ($changed) { 'updated' } else { 'unchanged' })"
            }

            foreach ($pair in $staged) {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pair[1]) | Out-Null
                Move-Item -LiteralPath $pair[0] -Destination $pair[1] -Force
            }

            $node = [System.Text.Json.Nodes.JsonNode]::Parse(($manifest | ConvertTo-Json -Depth 10 -Compress))
            Write-Utf8 -Path $manifestPath.FullName -Text (Format-StableJson -Node $node)
        }
        finally {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}

switch ($Action) {
    "Refresh" { Invoke-Refresh }
}
