using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Core;

/// <summary>
/// Reads and writes deck edit operations using the legacy flat plan JSON shape.
/// </summary>
public sealed class DeckEditOperationJsonConverter : JsonConverter<DeckEditOperation>
{
    /// <summary>
    /// Reads a persisted flat operation object into its typed operation case.
    /// </summary>
    public override DeckEditOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Deck edit operation must be a JSON object.");
        }

        string operation = ReadString(root, "operation")
            ?? ReadString(root, "type")
            ?? throw new JsonException("Deck edit operation is missing the operation discriminator.");
        string rationale = ReadString(root, "rationale") ?? "";
        string? cardName = ReadString(root, "cardName");
        string? quantityCategory = ReadString(root, "category");
        string? fromCategory = ReadString(root, "fromCategory");
        string? toCategory = ReadString(root, "toCategory");

        return operation switch
        {
            DeckEditOperations.AddCard => DeckEditOperation.AddCard(
                cardName ?? "",
                ReadInt32(root, "quantity"),
                quantityCategory,
                rationale),
            DeckEditOperations.RemoveCard => DeckEditOperation.RemoveCard(
                cardName ?? "",
                ReadInt32(root, "quantity"),
                quantityCategory,
                rationale),
            DeckEditOperations.SetCardQuantity => DeckEditOperation.SetCardQuantity(
                cardName ?? "",
                ReadInt32(root, "quantity"),
                quantityCategory,
                rationale),
            DeckEditOperations.MoveCard => DeckEditOperation.MoveCard(
                cardName ?? "",
                fromCategory,
                toCategory ?? "",
                rationale),
            DeckEditOperations.AddCardCategory => DeckEditOperation.AddCardCategory(
                cardName ?? "",
                quantityCategory ?? "",
                rationale),
            DeckEditOperations.RemoveCardCategory => DeckEditOperation.RemoveCardCategory(
                cardName ?? "",
                quantityCategory ?? "",
                rationale),
            DeckEditOperations.SetPrimaryCardCategory => DeckEditOperation.SetPrimaryCardCategory(
                cardName ?? "",
                quantityCategory ?? "",
                rationale),
            DeckEditOperations.CreateCategory => DeckEditOperation.CreateCategory(
                quantityCategory ?? "",
                ReadBoolean(root, "includedInDeck"),
                ReadBoolean(root, "includedInPrice"),
                rationale),
            DeckEditOperations.RenameCategory => DeckEditOperation.RenameCategory(
                fromCategory ?? "",
                toCategory ?? "",
                rationale),
            DeckEditOperations.DeleteCategory => DeckEditOperation.DeleteCategory(
                quantityCategory ?? "",
                toCategory,
                rationale),
            DeckEditOperations.UpdateDeckMetadata => DeckEditOperation.UpdateDeckMetadata(
                ReadString(root, "name"),
                ReadString(root, "format"),
                ReadString(root, "description"),
                rationale),
            _ => throw new JsonException($"Unknown deck edit operation '{operation}'.")
        };
    }

    /// <summary>
    /// Writes the operation as the flat JSON object expected by existing plan files and MCP clients.
    /// </summary>
    public override void Write(
        Utf8JsonWriter writer,
        DeckEditOperation value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("operation", value.Operation);
        WriteStringOrNull(writer, "cardName", value.CardName);
        WriteStringOrNull(writer, "replacementCardName", value.ReplacementCardName);
        WriteNumberOrNull(writer, "quantity", value.Quantity);
        WriteStringOrNull(writer, "category", value.Category);
        WriteStringOrNull(writer, "fromCategory", value.FromCategory);
        WriteStringOrNull(writer, "toCategory", value.ToCategory);
        WriteStringOrNull(writer, "name", value.Name);
        WriteStringOrNull(writer, "format", value.Format);
        WriteStringOrNull(writer, "description", value.Description);
        WriteBooleanOrNull(writer, "includedInDeck", value.IncludedInDeck);
        WriteBooleanOrNull(writer, "includedInPrice", value.IncludedInPrice);
        writer.WriteString("rationale", value.Rationale);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads a string property using case-insensitive matching to tolerate hand-authored files.
    /// </summary>
    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        throw new JsonException($"Deck edit operation property '{propertyName}' must be a string.");
    }

    /// <summary>
    /// Reads an integer property when present.
    /// </summary>
    private static int? ReadInt32(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
        {
            return result;
        }

        throw new JsonException($"Deck edit operation property '{propertyName}' must be an integer.");
    }

    /// <summary>
    /// Reads a boolean property when present.
    /// </summary>
    private static bool? ReadBoolean(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException($"Deck edit operation property '{propertyName}' must be a boolean.")
        };
    }

    /// <summary>
    /// Finds a property with exact or case-insensitive matching.
    /// </summary>
    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Writes a nullable string property.
    /// </summary>
    private static void WriteStringOrNull(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value);
    }

    /// <summary>
    /// Writes a nullable integer property.
    /// </summary>
    private static void WriteNumberOrNull(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    /// <summary>
    /// Writes a nullable boolean property.
    /// </summary>
    private static void WriteBooleanOrNull(Utf8JsonWriter writer, string propertyName, bool? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteBoolean(propertyName, value.Value);
    }
}
