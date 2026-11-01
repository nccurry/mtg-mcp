# Pure helpers for fixtures.ps1. They make no network calls, so tests dot-source this file.

# POST is allowed only for read-only lookups and the Archidekt login, whose response is never recorded.
$readOnlyPostPaths = @("/cards/collection", "/api/rest-auth/login/")

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

    if ($Deck.ContainsKey("description")) {
        $Deck["description"] = ""
    }

    if ($Deck["parentFolder"] -is [System.Text.Json.Nodes.JsonValue]) {
        $Deck["parentFolder"] = 0
    }
    elseif ($Deck["parentFolder"] -is [System.Text.Json.Nodes.JsonObject]) {
        $Deck["parentFolder"]["id"] = 0
    }

    $Deck["owner"]["id"] = 0
    [void] $Deck["owner"].Remove("frame")
}

# Deck summaries in a deck list or folder read carry the same owner id, deck text, and folder id as a whole deck.
function Clear-ListedDecks {
    param([System.Text.Json.Nodes.JsonArray] $Items)

    foreach ($item in $Items) {
        if ($item -isnot [System.Text.Json.Nodes.JsonObject]) {
            continue
        }

        if ($item["owner"] -is [System.Text.Json.Nodes.JsonObject]) {
            $item["owner"]["id"] = 0
        }

        if ($item["description"] -is [System.Text.Json.Nodes.JsonValue]) {
            $item["description"] = ""
        }

        if ($item["parentFolderId"] -is [System.Text.Json.Nodes.JsonValue]) {
            $item["parentFolderId"] = 0
        }

        # Edit time, card count, and color counts move whenever the owner edits the deck.
        if ($item["updatedAt"] -is [System.Text.Json.Nodes.JsonValue]) {
            $item["updatedAt"] = "2026-01-01T00:00:00Z"
        }

        if ($item["size"] -is [System.Text.Json.Nodes.JsonValue]) {
            $item["size"] = 0
        }

        if ($item["colors"] -is [System.Text.Json.Nodes.JsonObject]) {
            foreach ($color in @($item["colors"].Keys)) {
                $item["colors"][$color] = 0
            }
        }
    }
}

# A deck list comes most recently edited first, so rows are put in id order and the same decks stay after a trim.
function Sort-ListedDecks {
    param([System.Text.Json.Nodes.JsonArray] $Items)

    if ($Items.Count -eq 0 -or $Items[0] -isnot [System.Text.Json.Nodes.JsonObject] -or -not $Items[0].ContainsKey("parentFolderId")) {
        return
    }

    $sorted = @($Items | Sort-Object { $_["id"].GetValue[long]() })
    $Items.Clear()
    foreach ($item in $sorted) {
        $Items.Add($item.DeepClone())
    }
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
    if ($root -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($listName in @("results", "decks")) {
            if ($root[$listName] -is [System.Text.Json.Nodes.JsonArray]) {
                Sort-ListedDecks -Items $root[$listName]
            }
        }
    }

    if ((Test-Member $Entry "trimData") -and $root -is [System.Text.Json.Nodes.JsonObject]) {
        # Scryfall lists use data, Archidekt lists use results, and an Archidekt folder read uses decks.
        foreach ($listName in @("data", "results", "decks")) {
            $list = $root[$listName]
            while ($list -is [System.Text.Json.Nodes.JsonArray] -and $list.Count -gt $Entry.trimData) {
                $list.RemoveAt($list.Count - 1)
            }
        }
    }

    if ($root -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($listName in @("results", "decks")) {
            if ($root[$listName] -is [System.Text.Json.Nodes.JsonArray]) {
                Clear-ListedDecks -Items $root[$listName]
            }
        }
    }

    if ($root -is [System.Text.Json.Nodes.JsonObject] -and $root["owner"] -is [System.Text.Json.Nodes.JsonObject]) {
        Clear-DeckOwner -Deck $root
    }

    if ($root -is [System.Text.Json.Nodes.JsonObject] -and $root["cards"] -is [System.Text.Json.Nodes.JsonArray] -and
        $root["description"] -is [System.Text.Json.Nodes.JsonValue]) {
        # A named snapshot has deck text but no owner block.
        $root["description"] = ""
    }

    if ((Test-Member $Entry "keepCardNames") -and $root -is [System.Text.Json.Nodes.JsonObject]) {
        # Categories stay whole so excluded categories remain visible.
        Remove-UnlistedCards -Deck $root -KeepCardNames $Entry.keepCardNames
    }

    Remove-Volatile -Node $root
    return Format-StableJson -Node $root
}
