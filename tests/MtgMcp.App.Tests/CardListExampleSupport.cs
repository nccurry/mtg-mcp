using System.Text.Json;
using MtgMcp.App.CardLists;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Reads and fills the example requests that tool descriptions publish, so example tests can run them.
/// </summary>
internal static class CardListExampleSupport
{
    /// <summary>
    /// Reads the published description of one tool.
    /// </summary>
    internal static string DescriptionOf(string toolName)
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        return registered.Tools.Single(tool => tool.ProtocolTool.Name == toolName).ProtocolTool.Description!;
    }

    /// <summary>
    /// Reads each <c>Example (name): {json}</c> from a description, in order.
    /// </summary>
    internal static (string Name, string Json)[] ReadExamples(string description)
    {
        List<(string Name, string Json)> examples = [];
        int index = description.IndexOf(DeckToolExamples.ExamplePrefix, StringComparison.Ordinal);
        while (index >= 0)
        {
            int nameStart = index + DeckToolExamples.ExamplePrefix.Length;
            int nameEnd = description.IndexOf("): ", nameStart, StringComparison.Ordinal);
            int jsonStart = nameEnd + 3;
            int depth = 0;
            int end = jsonStart;
            do
            {
                depth += description[end] == '{' ? 1 : description[end] == '}' ? -1 : 0;
                end++;
            }
            while (depth > 0);

            examples.Add((description[nameStart..nameEnd], description[jsonStart..end]));
            index = description.IndexOf(DeckToolExamples.ExamplePrefix, end, StringComparison.Ordinal);
        }

        return [.. examples];
    }

    /// <summary>
    /// Puts real IDs and the deck's revision in place of the placeholders in an example.
    /// </summary>
    internal static string Fill(string example, DeckDocument deck, Guid snapshot, Guid card)
    {
        return example
            .Replace(DeckToolExamples.PlaceholderDeckId, deck.DeckId.ToString("D"), StringComparison.Ordinal)
            .Replace(CardListToolExamples.PlaceholderSnapshotId, snapshot.ToString("D"), StringComparison.Ordinal)
            .Replace(CardListToolExamples.PlaceholderCardId, card.ToString("D"), StringComparison.Ordinal)
            .Replace(
                $"\"expectedRevision\":{DeckToolExamples.PlaceholderRevision}",
                $"\"expectedRevision\":{deck.Revision}",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a JSON object of tool arguments.
    /// </summary>
    internal static Dictionary<string, JsonElement> Arguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
