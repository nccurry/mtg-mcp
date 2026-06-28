namespace MtgMcp.Core;

/// <summary>
/// Audits cached legality metadata and format construction rules.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Returns a structured legality audit using only cached workspace card snapshots.
    /// </summary>
    public async Task<DeckLegalityAudit> ValidateLegalityAsync(
        string workspaceId,
        bool includeExcluded,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return ValidateLegalitySnapshot(workspace, includeExcluded);
    }

    /// <summary>
    /// Returns a structured legality audit for an in-memory workspace snapshot.
    /// </summary>
    public DeckLegalityAudit ValidateLegalitySnapshot(DeckWorkspace workspace, bool includeExcluded)
    {
        string format = NormalizeLegalityFormat(workspace.Format);
        List<DeckCard> included = DeckServiceHelpers.IncludedCards(workspace).ToList();
        List<DeckCard> auditedCards = includeExcluded ? workspace.Cards.ToList() : included;
        CommandZoneContext commandZone = CommandZoneContext.FromWorkspace(workspace);
        DeckLegalityAudit audit = new()
        {
            WorkspaceId = workspace.Id,
            Format = format,
            IncludeExcluded = includeExcluded,
            IncludedCount = CountIncludedCards(included),
            AuditedCardRows = auditedCards.Count,
            CommandZone = BuildLegalityCommandZoneSummary(commandZone)
        };

        audit.Assumptions.Add(
            "Legality audit uses cached workspace snapshots only; run deck_refresh_card_metadata when metadata gaps are reported.");
        if (!includeExcluded)
        {
            audit.Assumptions.Add("Excluded categories are skipped for card-level legality and color-identity checks.");
        }

        AuditCommanderShape(audit, included, commandZone);
        AuditCachedCardLegality(audit, auditedCards, format);
        AuditColorIdentity(audit, included, commandZone);
        AuditCopyLimits(audit, included, format);
        AuditSideboard(audit, workspace, format);
        SortLegalityIssues(audit);
        return audit;
    }

    /// <summary>
    /// Adds Commander command-zone and deck-size findings.
    /// </summary>
    private static void AuditCommanderShape(
        DeckLegalityAudit audit,
        IReadOnlyList<DeckCard> included,
        CommandZoneContext commandZone)
    {
        if (!audit.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (audit.IncludedCount != 100)
        {
            audit.Warnings.Add(
                $"Commander decks normally contain exactly 100 included cards; this workspace has {audit.IncludedCount}.");
        }

        if (commandZone.CommanderNames.Count == 0)
        {
            audit.Errors.Add("Commander deck has no active non-Background command-zone commander.");
        }

        if (commandZone.CommandZoneCards.Count > 2)
        {
            audit.Warnings.Add(
                $"Commander deck has {commandZone.CommandZoneCards.Count} active command-zone cards; verify partner, Background, or companion setup manually.");
        }

        if (commandZone.HasPartnerPair || commandZone.HasBackgroundPair)
        {
            audit.Assumptions.Add(
                "Partner and Background legality is inferred from command-zone categories and cached type/oracle text.");
        }

        foreach (DeckCard card in included)
        {
            if (card.Companion)
            {
                audit.Warnings.Add(
                    $"{card.Name} is marked companion; companion condition legality is not evaluated by this audit.");
            }
        }
    }

    /// <summary>
    /// Adds card-level legality and missing-metadata findings.
    /// </summary>
    private static void AuditCachedCardLegality(
        DeckLegalityAudit audit,
        IReadOnlyList<DeckCard> cards,
        string format)
    {
        foreach (DeckCard card in cards)
        {
            CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
            string category = DeckCategoryOrdering.PrimaryCategory(card);
            if (string.IsNullOrWhiteSpace(snapshot.TypeLine))
            {
                audit.MetadataGaps.Add(CreateIssue(
                    "warning",
                    card,
                    category,
                    "Cached snapshot is missing type line metadata."));
            }

            if (!snapshot.Legalities.TryGetValue(format, out string? legality)
                || string.IsNullOrWhiteSpace(legality))
            {
                audit.MetadataGaps.Add(CreateIssue(
                    "warning",
                    card,
                    category,
                    $"Cached snapshot is missing {format} legality metadata."));
                continue;
            }

            if (!legality.Equals("legal", StringComparison.OrdinalIgnoreCase))
            {
                DeckLegalityIssue issue = CreateIssue(
                    "error",
                    card,
                    category,
                    $"{card.Name} is {legality} in {format}.");
                issue.Legality = legality;
                audit.CardLegalityIssues.Add(issue);
            }
        }
    }

    /// <summary>
    /// Adds Commander color-identity findings for active non-command-zone cards.
    /// </summary>
    private static void AuditColorIdentity(
        DeckLegalityAudit audit,
        IReadOnlyList<DeckCard> included,
        CommandZoneContext commandZone)
    {
        if (!audit.Format.Equals("commander", StringComparison.OrdinalIgnoreCase)
            || commandZone.CommandZoneCards.Count == 0)
        {
            return;
        }

        HashSet<string> commanderColors = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard commander in commandZone.CommandZoneCards)
        {
            foreach (string color in commander.Snapshot?.ColorIdentity ?? [])
            {
                if (IsColoredMana(color))
                {
                    commanderColors.Add(color.ToUpperInvariant());
                }
            }
        }

        foreach (DeckCard card in included)
        {
            if (commandZone.CommandZoneCards.Contains(card))
            {
                continue;
            }

            List<string> outsideColors = [];
            foreach (string color in card.Snapshot?.ColorIdentity ?? [])
            {
                string normalized = color.ToUpperInvariant();
                if (IsColoredMana(normalized) && !commanderColors.Contains(normalized))
                {
                    outsideColors.Add(normalized);
                }
            }

            if (outsideColors.Count == 0)
            {
                continue;
            }

            DeckLegalityIssue issue = CreateIssue(
                "error",
                card,
                DeckCategoryOrdering.PrimaryCategory(card),
                $"{card.Name} has color identity outside the commander's color identity.");
            issue.ColorIdentity = OrderColors(outsideColors);
            audit.ColorIdentityIssues.Add(issue);
        }
    }

    /// <summary>
    /// Adds singleton and constructed copy-limit findings for active cards.
    /// </summary>
    private static void AuditCopyLimits(
        DeckLegalityAudit audit,
        IReadOnlyList<DeckCard> included,
        string format)
    {
        Dictionary<string, CopyAggregate> copies = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in included)
        {
            string key = CopyIdentity(card);
            if (!copies.TryGetValue(key, out CopyAggregate? aggregate))
            {
                aggregate = new CopyAggregate(card);
                copies[key] = aggregate;
            }

            aggregate.Quantity += Math.Max(0, card.Quantity);
            aggregate.IsBasicLand = aggregate.IsBasicLand || BasicLandIdentity.IsBasicLand(card);
        }

        foreach (CopyAggregate aggregate in copies.Values)
        {
            if (aggregate.IsBasicLand)
            {
                continue;
            }

            if (format.Equals("commander", StringComparison.OrdinalIgnoreCase) && aggregate.Quantity > 1)
            {
                audit.CopyLimitIssues.Add(CreateIssue(
                    "error",
                    aggregate.Card,
                    DeckCategoryOrdering.PrimaryCategory(aggregate.Card),
                    $"Commander singleton violation: {aggregate.Card.Name} has {aggregate.Quantity} active copies.",
                    aggregate.Quantity));
            }
            else if (!format.Equals("commander", StringComparison.OrdinalIgnoreCase) && aggregate.Quantity > 4)
            {
                audit.CopyLimitIssues.Add(CreateIssue(
                    "error",
                    aggregate.Card,
                    DeckCategoryOrdering.PrimaryCategory(aggregate.Card),
                    $"{aggregate.Card.Name} has {aggregate.Quantity} active copies; constructed decks normally allow four.",
                    aggregate.Quantity));
            }
        }
    }

    /// <summary>
    /// Adds sideboard findings using primary Sideboard category rows.
    /// </summary>
    private static void AuditSideboard(DeckLegalityAudit audit, DeckWorkspace workspace, string format)
    {
        int sideboardCount = 0;
        foreach (DeckCard card in workspace.Cards)
        {
            if (DeckCategoryOrdering.PrimaryCategory(card).Equals(
                    DeckDefaults.Sideboard,
                    StringComparison.OrdinalIgnoreCase))
            {
                sideboardCount += Math.Max(0, card.Quantity);
            }
        }

        if (sideboardCount == 0)
        {
            return;
        }

        if (sideboardCount > 15)
        {
            audit.SideboardIssues.Add(new DeckLegalityIssue
            {
                Severity = "error",
                Category = DeckDefaults.Sideboard,
                Quantity = sideboardCount,
                Message = $"Sideboard has {sideboardCount} cards; constructed sideboards are limited to 15."
            });
        }

        if (format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            audit.SideboardIssues.Add(new DeckLegalityIssue
            {
                Severity = "warning",
                Category = DeckDefaults.Sideboard,
                Quantity = sideboardCount,
                Message = $"Workspace has {sideboardCount} sideboard cards; they are excluded from the active Commander deck unless the category is configured otherwise."
            });
        }
    }

    /// <summary>
    /// Builds command-zone summary facts for legality output.
    /// </summary>
    private static DeckLegalityCommandZoneSummary BuildLegalityCommandZoneSummary(CommandZoneContext commandZone)
    {
        List<string> colors = [];
        foreach (DeckCard card in commandZone.CommandZoneCards)
        {
            foreach (string color in card.Snapshot?.ColorIdentity ?? [])
            {
                if (IsColoredMana(color))
                {
                    AddDistinctColor(colors, color);
                }
            }
        }

        return new DeckLegalityCommandZoneSummary
        {
            DisplayName = commandZone.DisplayName,
            CommanderNames = commandZone.CommanderNames.ToList(),
            BackgroundNames = commandZone.BackgroundNames.ToList(),
            HasPartnerPair = commandZone.HasPartnerPair,
            HasBackgroundPair = commandZone.HasBackgroundPair,
            ColorIdentity = OrderColors(colors),
            CardRows = commandZone.CommandZoneCards.Count
        };
    }

    /// <summary>
    /// Normalizes format aliases to Scryfall legality keys.
    /// </summary>
    private static string NormalizeLegalityFormat(string? format)
    {
        string normalized = string.IsNullOrWhiteSpace(format)
            ? "commander"
            : format.Trim().ToLowerInvariant();
        return normalized is "edh" ? "commander" : normalized;
    }

    /// <summary>
    /// Counts active card quantities.
    /// </summary>
    private static int CountIncludedCards(IEnumerable<DeckCard> cards)
    {
        int total = 0;
        foreach (DeckCard card in cards)
        {
            total += Math.Max(0, card.Quantity);
        }

        return total;
    }

    /// <summary>
    /// Creates one card-scoped legality issue.
    /// </summary>
    private static DeckLegalityIssue CreateIssue(
        string severity,
        DeckCard card,
        string category,
        string message,
        int? quantity = null)
    {
        return new DeckLegalityIssue
        {
            Severity = severity,
            CardName = card.Name,
            Category = category,
            Quantity = quantity ?? card.Quantity,
            ColorIdentity = OrderColors(card.Snapshot?.ColorIdentity ?? []),
            Message = message,
            ScryfallUri = card.Snapshot?.ScryfallUri
        };
    }

    /// <summary>
    /// Builds a stable card identity for copy-limit aggregation.
    /// </summary>
    private static string CopyIdentity(DeckCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.ScryfallOracleId))
        {
            return $"oracle:{card.ScryfallOracleId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(card.Snapshot?.ScryfallUri))
        {
            return $"uri:{card.Snapshot.ScryfallUri.Trim()}";
        }

        return $"name:{card.Name.Trim()}";
    }

    /// <summary>
    /// Adds one color symbol to a list once.
    /// </summary>
    private static void AddDistinctColor(List<string> colors, string color)
    {
        string normalized = color.ToUpperInvariant();
        if (!colors.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            colors.Add(normalized);
        }
    }

    /// <summary>
    /// Returns true for colored mana symbols used in color identity.
    /// </summary>
    private static bool IsColoredMana(string value)
    {
        return value.Equals("W", StringComparison.OrdinalIgnoreCase)
            || value.Equals("U", StringComparison.OrdinalIgnoreCase)
            || value.Equals("B", StringComparison.OrdinalIgnoreCase)
            || value.Equals("R", StringComparison.OrdinalIgnoreCase)
            || value.Equals("G", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders color symbols in WUBRG order.
    /// </summary>
    private static List<string> OrderColors(IEnumerable<string> colors)
    {
        List<string> result = [];
        foreach (string color in new[] { "W", "U", "B", "R", "G" })
        {
            if (colors.Any(value => value.Equals(color, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(color);
            }
        }

        return result;
    }

    /// <summary>
    /// Sorts issue lists for stable output.
    /// </summary>
    private static void SortLegalityIssues(DeckLegalityAudit audit)
    {
        SortIssues(audit.CardLegalityIssues);
        SortIssues(audit.ColorIdentityIssues);
        SortIssues(audit.CopyLimitIssues);
        SortIssues(audit.SideboardIssues);
        SortIssues(audit.MetadataGaps);
    }

    /// <summary>
    /// Sorts one issue list by severity, card name, and message.
    /// </summary>
    private static void SortIssues(List<DeckLegalityIssue> issues)
    {
        issues.Sort(static (left, right) =>
        {
            int severity = SeverityRank(left.Severity).CompareTo(SeverityRank(right.Severity));
            if (severity != 0)
            {
                return severity;
            }

            int name = string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
            return name != 0
                ? name
                : string.Compare(left.Message, right.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Ranks errors before warnings.
    /// </summary>
    private static int SeverityRank(string severity)
    {
        return severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>
    /// Tracks active copies for one card identity.
    /// </summary>
    private sealed class CopyAggregate
    {
        /// <summary>
        /// Creates a copy aggregate around the first observed row.
        /// </summary>
        public CopyAggregate(DeckCard card)
        {
            Card = card;
        }

        /// <summary>
        /// Gets the representative card row.
        /// </summary>
        public DeckCard Card { get; }

        /// <summary>
        /// Gets or sets aggregate active quantity.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Gets or sets whether any aggregate row is a basic land.
        /// </summary>
        public bool IsBasicLand { get; set; }
    }
}
