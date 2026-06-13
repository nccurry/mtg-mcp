
namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains the main fake card catalog used by deck intelligence tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Provides card data for deck intelligence tests.
    /// </summary>
    private sealed partial class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Gets search queries sent to the fake catalog.
        /// </summary>
        public List<string> SearchQueries { get; } = [];

        /// <summary>
        /// Gets or sets whether Game Changer search throws.
        /// </summary>
        public bool ThrowOnGameChangerSearch { get; init; }

        /// <summary>
        /// Gets or sets whether Game Changer search should simulate caller cancellation.
        /// </summary>
        public bool CancelGameChangerSearch { get; init; }

        /// <summary>
        /// Gets or sets whether single-card lookup throws.
        /// </summary>
        public bool ThrowOnGetCard { get; init; }

        /// <summary>
        /// Gets or sets whether single-card lookup should simulate caller cancellation.
        /// </summary>
        public bool CancelGetCard { get; init; }

        /// <summary>
        /// Searches fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            SearchQueries.Add(query);
            return Task.FromResult(SearchFakeCards(query, cancellationToken));
        }

        /// <summary>
        /// Searches fake cards from a semantic request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            SearchQueries.Add(DescribeSearchRequest(request));
            return Task.FromResult(SearchFakeCards(BuildFakeQuery(request), cancellationToken));
        }

        /// <summary>
        /// Returns fake cards for a query-like test fixture string.
        /// </summary>
        private IReadOnlyList<CardSearchResult> SearchFakeCards(
            string query,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results;
            if (query.Contains("is:game-changer", StringComparison.OrdinalIgnoreCase))
            {
                if (CancelGameChangerSearch)
                {
                    throw new TaskCanceledException("Caller cancelled Game Changer search.");
                }

                if (ThrowOnGameChangerSearch)
                {
                    throw new HttpRequestException("Scryfall unavailable.");
                }

                results = [new CardSearchResult { Name = "Mana Crypt" }];
            }
            else if (query.Contains("commander-candidates", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Alesha, Who Smiles at Death" },
                    new CardSearchResult { Name = "Tatyova, Benthic Druid" },
                    new CardSearchResult { Name = "Glissa Sunslayer" },
                    new CardSearchResult { Name = "Roon of the Hidden Realm" }
                ];
            }
            else if (query.Contains("t:land", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Temple of Silence" },
                    new CardSearchResult { Name = "Command Tower" }
                ];
            }
            else if (query.Contains("discard", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Geth's Grimoire" },
                    new CardSearchResult { Name = "Waste Not" },
                    new CardSearchResult { Name = "Syphon Mind" },
                    new CardSearchResult { Name = "Torment of Hailfire" },
                    new CardSearchResult { Name = "Zulaport Cutthroat" },
                    new CardSearchResult { Name = "Mirkwood Bats" }
                ];
            }
            else if (query.Contains("hexproof", StringComparison.OrdinalIgnoreCase)
                || query.Contains("shroud", StringComparison.OrdinalIgnoreCase)
                || query.Contains("phase out", StringComparison.OrdinalIgnoreCase)
                || query.Contains("indestructible", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Lightning Greaves" }];
            }
            else if (query.Contains("add", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Arcane Signet" }];
            }
            else if (query.Contains("scry", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Opt" }
                ];
            }
            else if (query.Contains("draw", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Rhystic Study" },
                    new CardSearchResult { Name = "Necropotence" },
                    new CardSearchResult { Name = "Phyrexian Arena" }
                ];
            }
            else if (query.Contains("destroy target", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Hero's Downfall" }
                ];
            }
            else if (query.Contains("each opponent", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each player", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each creature", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Syphon Mind" },
                    new CardSearchResult { Name = "Blasphemous Act" }
                ];
            }
            else if (query.Contains("goad", StringComparison.OrdinalIgnoreCase)
                || query.Contains("monarch", StringComparison.OrdinalIgnoreCase)
                || query.Contains("vote", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Court of Ambition" }];
            }
            else if (query.Contains("destroy all tokens", StringComparison.OrdinalIgnoreCase)
                || query.Contains("creatures can't attack", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Illness in the Ranks" },
                    new CardSearchResult { Name = "Crawlspace" }
                ];
            }
            else if (query.Contains("date>=", StringComparison.OrdinalIgnoreCase)
                || query.Contains("set:", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Season of Loss" }];
            }
            else if (query.Contains("legal:commander", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Lightning Greaves" }];
            }
            else
            {
                results = [];
            }

            return results;
        }

        /// <summary>
        /// Converts semantic test requests into fixture selectors.
        /// </summary>
        private static string BuildFakeQuery(CardSearchRequest request)
        {
            return request.Preset switch
            {
                CardSearchPreset.RawQuery => request.RawQuery ?? "",
                CardSearchPreset.CommanderGameChangers => "is:game-changer",
                CardSearchPreset.CommanderCandidates => "commander-candidates",
                CardSearchPreset.Role => RoleFixtureQuery(request.Role),
                CardSearchPreset.CommanderProtectionEquipment => "hexproof shroud",
                CardSearchPreset.CommanderProtectionSpell => "indestructible phase out",
                CardSearchPreset.DrawDiscard => "discard",
                CardSearchPreset.CardDraw => "draw",
                CardSearchPreset.DiscardSynergy => "discard",
                CardSearchPreset.PoliticalChoices => "goad monarch vote",
                CardSearchPreset.PoliticalTableEffects => "each opponent",
                CardSearchPreset.WholeTablePolitics => "goad monarch vote each opponent",
                CardSearchPreset.WholeTableEffects => "each player each creature",
                CardSearchPreset.TableWideInteraction => "each opponent each player each creature",
                CardSearchPreset.TokenDefenseSweepers => "destroy all tokens",
                CardSearchPreset.TokenDefensePillowfort => "creatures can't attack",
                CardSearchPreset.GraveyardHate => "graveyard",
                CardSearchPreset.Finishers => "each opponent loses",
                CardSearchPreset.LessSaltyValue => "draw",
                CardSearchPreset.BroadUseful => "legal:commander",
                CardSearchPreset.BroadUsefulFallback => "draw destroy target add",
                CardSearchPreset.RecentCards => $"date>={request.Since:yyyy-MM-dd} set:{request.SetCode}",
                _ => ""
            };
        }

        /// <summary>
        /// Converts a role request into a fixture selector.
        /// </summary>
        private static string RoleFixtureQuery(string? role)
        {
            return (role ?? "").ToLowerInvariant() switch
            {
                "lands" => "t:land",
                "ramp" => "add",
                "draw" => "draw",
                "interaction" => "destroy target",
                "board wipes" => "each creature",
                "protection" => "hexproof",
                "card selection" => "scry",
                _ => "legal:commander"
            };
        }

        /// <summary>
        /// Describes a semantic request without adapter query syntax.
        /// </summary>
        private static string DescribeSearchRequest(CardSearchRequest request)
        {
            return request.Preset switch
            {
                CardSearchPreset.RawQuery => request.RawQuery ?? "",
                CardSearchPreset.Role => $"Role:{request.Role}",
                CardSearchPreset.RecentCards => $"RecentCards:{request.Since:yyyy-MM-dd}",
                CardSearchPreset.CommanderCandidates => $"CommanderCandidates:{request.ColorIdentity}:{request.ExactColorIdentity}",
                _ => request.Preset.ToString()
            };
        }

        /// <summary>
        /// Gets a fake card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            if (CancelGetCard)
            {
                throw new TaskCanceledException("Caller cancelled card lookup.");
            }

            if (ThrowOnGetCard)
            {
                throw new HttpRequestException("Scryfall unavailable.");
            }

            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets fake cards by names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            if (CancelGetCard)
            {
                throw new TaskCanceledException("Caller cancelled card lookup.");
            }

            if (ThrowOnGetCard)
            {
                throw new HttpRequestException("Scryfall unavailable.");
            }

            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                cards[name] = CreateCard(name);
            }

            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(cards);
        }

        /// <summary>
        /// Gets fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

    }

}
