using System.Text;

namespace MtgMcp.Core;

/// <summary>
/// Provides deck exporter behavior.
/// </summary>
public sealed class DeckExporter
{
    /// <summary>
    /// Exports the workspace using the existing grouped plain-text format.
    /// </summary>
    public static string Export(DeckWorkspace workspace)
    {
        return Export(workspace, new DeckExportOptions());
    }

    /// <summary>
    /// Exports the workspace in text or Markdown while preserving grouped text defaults.
    /// </summary>
    public static string Export(DeckWorkspace workspace, DeckExportOptions options)
    {
        StringBuilder builder = new();
        Dictionary<string, DeckCategory> categories = DeckCategoryInclusion.BuildCategoryMap(workspace);
        string format = string.IsNullOrWhiteSpace(options.Format)
            ? "text"
            : options.Format.Trim().ToLowerInvariant();
        bool markdown = format is "markdown" or "markdown-links";
        bool markdownLinks = format is "markdown-links";

        if (!options.IncludeCategories)
        {
            List<DeckCard> cards = [];
            foreach (DeckCard card in workspace.Cards)
            {
                string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
                if (options.IncludedOnly && !DeckCategoryInclusion.IsIncludedInDeck(categories, primaryCategory))
                {
                    continue;
                }

                cards.Add(card);
            }

            cards.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            foreach (DeckCard card in cards)
            {
                AppendCardLine(builder, card, markdown, markdownLinks);
            }

            return builder.ToString().TrimEnd();
        }

        foreach (DeckCategory category in workspace.Categories)
        {
            List<DeckCard> cards = [];
            foreach (DeckCard card in workspace.Cards)
            {
                string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
                if (!primaryCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (options.IncludedOnly && !DeckCategoryInclusion.IsIncludedInDeck(categories, primaryCategory))
                {
                    continue;
                }

                cards.Add(card);
            }

            cards.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

            if (cards.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            if (options.IncludeCategories)
            {
                builder.AppendLine(markdown ? $"## {category.Name}" : category.Name);
            }

            foreach (DeckCard card in cards)
            {
                AppendCardLine(builder, card, markdown, markdownLinks);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends one card row in plain text or Markdown.
    /// </summary>
    private static void AppendCardLine(
        StringBuilder builder,
        DeckCard card,
        bool markdown,
        bool markdownLinks)
    {
        if (markdown)
        {
            builder.Append("- ");
        }

        builder.Append(card.Quantity);
        builder.Append(' ');
        if (markdownLinks)
        {
            builder.Append('[');
            builder.Append(EscapeMarkdownLinkText(card.Name));
            builder.Append("](");
            builder.Append(ScryfallLink(card));
            builder.Append(')');
        }
        else
        {
            builder.Append(card.Name);
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Returns a card's known Scryfall page or a deterministic exact-name search URL.
    /// </summary>
    private static string ScryfallLink(DeckCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.Snapshot?.ScryfallUri))
        {
            return card.Snapshot.ScryfallUri;
        }

        return "https://scryfall.com/search?as=grid&order=name&q="
            + Uri.EscapeDataString($"!\"{card.Name}\"");
    }

    /// <summary>
    /// Escapes characters that would break Markdown link text.
    /// </summary>
    private static string EscapeMarkdownLinkText(string value)
    {
        return value.Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
