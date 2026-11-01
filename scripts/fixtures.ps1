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

# POST is allowed only for provider routes that are read-only lookups.
$readOnlyPostPaths = @("/cards/collection")

# Properties removed wherever they appear, because they identify a person or change without a card change.
$removedProperties = @(
    "username", "avatar", "avatarUrl", "avatarURL", "email",
    "image_uris", "purchase_uris", "related_uris", "edhrec_rank", "penny_rank"
)

$writerOptions = [System.Text.Json.JsonSerializerOptions]::new()
$writerOptions.WriteIndented = $true
$writerOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function Remove-Volatile {
    param([System.Text.Json.Nodes.JsonNode] $Node)

    if ($Node -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($name in @($Node.Keys)) {
            if ($removedProperties -contains $name) {
                [void] $Node.Remove($name)
            }
            elseif ($name -eq "prices" -and $Node[$name] -is [System.Text.Json.Nodes.JsonObject]) {
                $prices = $Node[$name]
                foreach ($key in @($prices.Keys)) {
                    $prices[$key] = $null
                }
            }
            else {
                Remove-Volatile -Node $Node[$name]
            }
        }
    }
    elseif ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        foreach ($item in $Node) {
            Remove-Volatile -Node $item
        }
    }
}

function ConvertTo-StableJson {
    param([string] $Text, $TrimData)

    $root = [System.Text.Json.Nodes.JsonNode]::Parse($Text)
    if ($null -ne $TrimData -and $root -is [System.Text.Json.Nodes.JsonObject] -and $root["data"] -is [System.Text.Json.Nodes.JsonArray]) {
        $data = $root["data"]
        while ($data.Count -gt $TrimData) {
            $data.RemoveAt($data.Count - 1)
        }
    }

    Remove-Volatile -Node $root
    return ($root.ToJsonString($writerOptions) -replace "`r`n", "`n") + "`n"
}

function Get-Sha256 {
    param([string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8 {
    param([string] $Path, [string] $Text)

    [System.IO.File]::WriteAllBytes($Path, [System.Text.UTF8Encoding]::new($false).GetBytes($Text))
}

function Invoke-ProviderRead {
    param($Entry)

    $uri = [System.Uri] $Entry.url
    $method = $Entry.method.ToUpperInvariant()
    $isReadOnlyPost = $method -eq "POST" -and ($readOnlyPostPaths -contains $uri.AbsolutePath)
    if ($method -ne "GET" -and -not $isReadOnlyPost) {
        throw "Refusing $method $($Entry.url): fixtures are read with GET only."
    }

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), $uri)
    $request.Headers.TryAddWithoutValidation("User-Agent", $userAgent) | Out-Null
    $request.Headers.TryAddWithoutValidation("Accept", "application/json") | Out-Null
    if ($isReadOnlyPost) {
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
    foreach ($manifestPath in $manifestPaths) {
        $manifest = Get-Content -LiteralPath $manifestPath.FullName -Raw | ConvertFrom-Json -DateKind String
        foreach ($entry in $manifest.entries) {
            $path = Join-Path $manifestPath.Directory.FullName $entry.file
            $before = if (Test-Path -LiteralPath $path) { Get-Sha256 -Path $path } else { "" }
            if ($entry.refresh) {
                if (-not $first) {
                    Start-Sleep -Milliseconds $PaceMilliseconds
                }

                $first = $false
                $trim = if ($entry.PSObject.Properties.Name -contains "trimData") { $entry.trimData } else { $null }
                $text = ConvertTo-StableJson -Text (Invoke-ProviderRead -Entry $entry) -TrimData $trim
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
                Write-Utf8 -Path $path -Text $text
            }

            $after = Get-Sha256 -Path $path
            if ($after -ne $before) {
                $entry.capturedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
            }

            $entry.sha256 = $after
            Write-Host "$($entry.id): $(if ($after -eq $before) { 'unchanged' } else { 'updated' })"
        }

        $json = ConvertTo-StableJson -Text ($manifest | ConvertTo-Json -Depth 10 -Compress) -TrimData $null
        Write-Utf8 -Path $manifestPath.FullName -Text $json
    }
}

switch ($Action) {
    "Refresh" { Invoke-Refresh }
}
