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
        if (markdownLinks && !string.IsNullOrWhiteSpace(card.Snapshot?.ScryfallUri))
        {
            builder.Append('[');
            builder.Append(card.Name);
            builder.Append("](");
            builder.Append(card.Snapshot.ScryfallUri);
            builder.Append(')');
        }
        else
        {
            builder.Append(card.Name);
        }

        builder.AppendLine();
    }
}
