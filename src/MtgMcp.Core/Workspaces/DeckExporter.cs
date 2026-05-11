using System.Text;

namespace MtgMcp.Core;

/// <summary>
/// Provides deck exporter behavior.
/// </summary>
public sealed class DeckExporter
{
    /// <summary>
    /// Exports the workspace.
    /// </summary>
    public static string Export(DeckWorkspace workspace)
    {
        StringBuilder builder = new();
        foreach (DeckCategory category in workspace.Categories)
        {
            List<DeckCard> cards = workspace
                .Cards.Where(card =>
                    DeckCategoryOrdering.PrimaryCategory(card).Equals(category.Name, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cards.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(category.Name);
            foreach (DeckCard card in cards)
            {
                builder.Append(card.Quantity);
                builder.Append(' ');
                builder.AppendLine(card.Name);
            }
        }

        return builder.ToString().TrimEnd();
    }
}
