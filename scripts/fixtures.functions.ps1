# Pure helpers for fixtures.ps1. They make no network calls, so tests dot-source this file.

# POST is allowed only for provider routes that are read-only lookups.
$readOnlyPostPaths = @("/cards/collection")

# Properties removed wherever they appear, because they identify a person or change without a card change.
$removedProperties = @(
    "username", "avatar", "avatarUrl", "avatarURL", "email",
    "image_uris", "purchase_uris", "related_uris", "edhrec_rank", "penny_rank",
    "ckAffiliate", "tcgAffiliate", "referrerEnum", "viewCount", "commentRoot"
)

$writerOptions = [System.Text.Json.JsonSerializerOptions]::new()
$writerOptions.WriteIndented = $true
$writerOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function Assert-ReadOnlyRequest {
    param([string] $Method, [string] $Path)

    $isReadOnlyPost = $Method -eq "POST" -and ($readOnlyPostPaths -contains $Path)
    if ($Method -ne "GET" -and -not $isReadOnlyPost) {
        throw "Refusing $Method $Path`: fixtures are read with GET only."
    }
}

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

function Test-Member {
    param($Entry, [string] $Name)

    return $null -ne $Entry -and $Entry.PSObject.Properties.Name -contains $Name
}

# The deck text names the owner's migration source and workspace, so it is replaced; the owner id and folder id are zeroed.
function Clear-DeckOwner {
    param([System.Text.Json.Nodes.JsonObject] $Deck)

    $Deck["description"] = ""
    if ($Deck["parentFolder"] -is [System.Text.Json.Nodes.JsonValue]) {
        $Deck["parentFolder"] = 0
    }

    $Deck["owner"]["id"] = 0
    [void] $Deck["owner"].Remove("frame")
}

function Remove-UnlistedCards {
    param([System.Text.Json.Nodes.JsonObject] $Deck, [string[]] $KeepCardNames)

    $cards = $Deck["cards"]
    for ($index = $cards.Count - 1; $index -ge 0; $index--) {
        if ($KeepCardNames -notcontains $cards[$index]["card"]["oracleCard"]["name"].GetValue[string]()) {
            $cards.RemoveAt($index)
        }
    }
}

function Format-StableJson {
    param([System.Text.Json.Nodes.JsonNode] $Node)

    return ($Node.ToJsonString($writerOptions) -replace "`r`n", "`n") + "`n"
}

function ConvertTo-StableJson {
    param([string] $Text, $Entry)

    $root = [System.Text.Json.Nodes.JsonNode]::Parse($Text)
    if ((Test-Member $Entry "trimData") -and $root -is [System.Text.Json.Nodes.JsonObject]) {
        # Scryfall lists use data and Archidekt lists use results.
        foreach ($listName in @("data", "results")) {
            $list = $root[$listName]
            while ($list -is [System.Text.Json.Nodes.JsonArray] -and $list.Count -gt $Entry.trimData) {
                $list.RemoveAt($list.Count - 1)
            }
        }
    }

    if ($root -is [System.Text.Json.Nodes.JsonObject] -and $root["owner"] -is [System.Text.Json.Nodes.JsonObject]) {
        Clear-DeckOwner -Deck $root
    }

    if ((Test-Member $Entry "keepCardNames") -and $root -is [System.Text.Json.Nodes.JsonObject]) {
        # Categories stay whole so excluded categories remain visible.
        Remove-UnlistedCards -Deck $root -KeepCardNames $Entry.keepCardNames
    }

    Remove-Volatile -Node $root
    return Format-StableJson -Node $root
}
