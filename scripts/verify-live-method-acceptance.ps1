[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = [System.IO.Path]::GetFullPath($DataRoot)
$marker = Join-Path $root ".mtg-mcp-live-acceptance"
$reportPath = Join-Path $root "live-method-results.json"
if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "The live acceptance root is not marked."
}

if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "The live method journal was not produced."
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schemaVersion -ne 1) {
    throw "The live method journal schema is unsupported."
}

if ($report.testedCommit -notmatch '^[0-9a-f]{40}$' -or
    $report.testedCommit -ne $env:MTGMCP_LIVE_ACCEPTANCE_COMMIT) {
    throw "The live method journal is not pinned to the current tested commit."
}

if ([string]::IsNullOrWhiteSpace($report.packageVersion) -or
    $report.packageVersion -ne $env:MTGMCP_E2E_VERSION) {
    throw "The live method journal is not pinned to the installed package version."
}

if ($report.capabilityResourceStatus -ne "live-pass") {
    throw "The capability resource has not passed packaged live acceptance."
}

$records = @($report.records)
$duplicates = @($records | Group-Object tool | Where-Object Count -ne 1)
if ($duplicates.Count -gt 0) {
    throw "The live method journal contains duplicate tool rows."
}

$live = @($records | Where-Object status -eq "live-pass")
$fixtureOnly = @($records | Where-Object status -eq "fixture-only-owner-approved")
$fixtureBacked = @($records | Where-Object status -eq "fixture-backed-owner-approved")
$pending = @($records | Where-Object status -eq "pending-provider-generation")
$incomplete = @($records | Where-Object status -notin @("live-pass", "fixture-only-owner-approved", "fixture-backed-owner-approved", "pending-provider-generation"))
if ($records.Count -ne 93 -or $live.Count -ne 88 -or $fixtureOnly.Count -ne 2 -or $fixtureBacked.Count -ne 2 -or $pending.Count -ne 1 -or $incomplete.Count -ne 0) {
    $summary = $records |
        Group-Object status |
        Sort-Object Name |
        ForEach-Object { "$($_.Name)=$($_.Count)" }
    throw "Live method acceptance is incomplete: $($summary -join ', ')."
}

$expectedFixtureOnly = @(
    "playgroup_game_events_batch_create",
    "playgroup_live_session_create"
)
$actualFixtureOnly = @($fixtureOnly.tool | Sort-Object)
if (Compare-Object $expectedFixtureOnly $actualFixtureOnly) {
    throw "The fixture-only disposition does not match the reviewed Playgroup write boundary."
}

$expectedFixtureBacked = @(
    "scryfall_corpus_delete",
    "scryfall_corpus_sync"
)
$actualFixtureBacked = @($fixtureBacked.tool | Sort-Object)
if (Compare-Object $expectedFixtureBacked $actualFixtureBacked) {
    throw "The fixture-backed disposition does not match the reviewed Scryfall lifecycle boundary."
}

if ($pending[0].tool -ne "scryfall_corpus_rollback") {
    throw "The pending provider-generation disposition is not the reviewed Scryfall rollback boundary."
}

Write-Host "Live method acceptance complete: resource=1, live-pass=88, fixture-backed=2, pending-provider-generation=1, fixture-only=2."
