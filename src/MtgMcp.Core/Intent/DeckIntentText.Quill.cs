using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.Core;

/// <summary>
/// Converts and edits Archidekt Quill description deltas.
/// </summary>
public static partial class DeckIntentText
{
    /// <summary>
    /// Converts Archidekt description storage to plain text.
    /// </summary>
    public static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        return TryReadQuillDescription(description, out _, out JsonArray ops)
            ? ToPlainText(ops)
            : description;
    }

    /// <summary>
    /// Converts plain text back to an Archidekt-compatible description shape.
    /// </summary>
    public static string FromPlainText(string plainText, bool asQuillDelta)
    {
        if (!asQuillDelta)
        {
            return plainText;
        }

        string text = plainText.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? plainText
            : plainText + Environment.NewLine;
        object delta = new
        {
            ops = new[]
            {
                new { insert = text }
            }
        };
        return JsonSerializer.Serialize(delta);
    }

    /// <summary>
    /// Determines whether text looks like a Quill delta.
    /// </summary>
    private static bool IsQuillDelta(string? description)
    {
        string text = description?.Trim() ?? "";
        return text.StartsWith('{')
            && text.Contains("\"ops\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates a Quill description while preserving existing ops.
    /// </summary>
    private static bool TryUpsertQuillDescription(
        string? description,
        string normalizedIntent,
        out string quillDescription)
    {
        quillDescription = "";
        if (!TryReadQuillDescription(description, out JsonObject root, out JsonArray ops))
        {
            return false;
        }

        string plainText = ToPlainText(ops);
        string searchableText = plainText.TrimEnd();
        string replacement;
        int start;
        int end;

        if (TryFindBlock(searchableText, out start, out end))
        {
            replacement = ToQuillText(normalizedIntent);
        }
        else if (searchableText.Length == 0)
        {
            start = 0;
            end = 0;
            replacement = ToQuillText(normalizedIntent);
        }
        else
        {
            start = searchableText.Length;
            end = searchableText.Length;
            replacement = "\n\n" + ToQuillText(normalizedIntent);
        }

        JsonArray updatedOps = SpliceTextOps(ops, start, end, replacement);
        EnsureTrailingNewLine(updatedOps);
        root["ops"] = updatedOps;
        quillDescription = JsonSerializer.Serialize(root);
        return true;
    }

    /// <summary>
    /// Clears an intent section from a Quill description while preserving existing ops.
    /// </summary>
    private static bool TryClearQuillDescription(string? description, out string quillDescription)
    {
        quillDescription = description ?? "";
        if (!TryReadQuillDescription(description, out JsonObject root, out JsonArray ops))
        {
            return false;
        }

        string searchableText = ToPlainText(ops).TrimEnd();
        if (!TryFindBlock(searchableText, out int start, out int end))
        {
            return true;
        }

        JsonArray updatedOps = SpliceTextOps(ops, RewindSeparator(searchableText, start), end, "");
        EnsureTrailingNewLine(updatedOps);
        root["ops"] = updatedOps;
        quillDescription = JsonSerializer.Serialize(root);
        return true;
    }

    /// <summary>
    /// Reads a mutable Quill root and ops array.
    /// </summary>
    private static bool TryReadQuillDescription(
        string? description,
        out JsonObject root,
        out JsonArray ops)
    {
        root = new JsonObject();
        ops = new JsonArray();
        if (!IsQuillDelta(description))
        {
            return false;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(description!);
            if (node is not JsonObject rootObject
                || rootObject["ops"] is not JsonArray opArray)
            {
                return false;
            }

            root = rootObject;
            ops = opArray;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts Quill ops to plain text, ignoring embedded objects.
    /// </summary>
    private static string ToPlainText(JsonArray ops)
    {
        List<string> parts = [];
        foreach (JsonNode? opNode in ops)
        {
            if (opNode is JsonObject op && TryGetStringInsert(op, out string text))
            {
                parts.Add(text);
            }
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// Applies a text replacement across string insert ops.
    /// </summary>
    private static JsonArray SpliceTextOps(JsonArray ops, int start, int end, string replacement)
    {
        JsonArray updatedOps = [];
        int cursor = 0;
        bool inserted = false;

        foreach (JsonNode? opNode in ops)
        {
            if (opNode is not JsonObject op || !TryGetStringInsert(op, out string text))
            {
                if (!inserted && start <= cursor)
                {
                    AddPlainInsert(updatedOps, replacement);
                    inserted = true;
                }

                AddClonedNode(updatedOps, opNode);
                continue;
            }

            int opStart = cursor;
            int opEnd = cursor + text.Length;
            if (!inserted && start <= opStart)
            {
                AddPlainInsert(updatedOps, replacement);
                inserted = true;
            }

            if (opEnd <= start || opStart >= end)
            {
                AddClonedNode(updatedOps, opNode);
                cursor = opEnd;
                continue;
            }

            int beforeLength = Math.Clamp(start - opStart, 0, text.Length);
            if (beforeLength > 0)
            {
                updatedOps.Add(CloneStringOp(op, text[..beforeLength]));
            }

            if (!inserted)
            {
                AddPlainInsert(updatedOps, replacement);
                inserted = true;
            }

            int afterStart = Math.Clamp(end - opStart, 0, text.Length);
            if (afterStart < text.Length)
            {
                updatedOps.Add(CloneStringOp(op, text[afterStart..]));
            }

            cursor = opEnd;
        }

        if (!inserted)
        {
            AddPlainInsert(updatedOps, replacement);
        }

        return updatedOps;
    }

    /// <summary>
    /// Rewinds over separator newlines that commonly precede an appended intent block.
    /// </summary>
    private static int RewindSeparator(string text, int start)
    {
        int index = start;
        int removed = 0;
        while (index > 0 && removed < 2 && text[index - 1] == '\n')
        {
            index--;
            removed++;
        }

        return index;
    }

    /// <summary>
    /// Adds a cloned node when one exists.
    /// </summary>
    private static void AddClonedNode(JsonArray ops, JsonNode? node)
    {
        if (node is not null)
        {
            ops.Add(node.DeepClone());
        }
    }

    /// <summary>
    /// Adds a plain insert op when replacement text is present.
    /// </summary>
    private static void AddPlainInsert(JsonArray ops, string text)
    {
        if (text.Length > 0)
        {
            ops.Add(new JsonObject { ["insert"] = text });
        }
    }

    /// <summary>
    /// Clones a string op while replacing its inserted text.
    /// </summary>
    private static JsonObject CloneStringOp(JsonObject op, string text)
    {
        JsonObject clone = (JsonObject)op.DeepClone();
        clone["insert"] = text;
        return clone;
    }

    /// <summary>
    /// Reads a string insert value.
    /// </summary>
    private static bool TryGetStringInsert(JsonObject op, out string text)
    {
        text = "";
        if (op["insert"] is JsonValue insert
            && insert.TryGetValue(out string? value))
        {
            text = value ?? "";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ensures Quill text uses line feeds.
    /// </summary>
    private static string ToQuillText(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    /// <summary>
    /// Ensures the delta ends with a text newline.
    /// </summary>
    private static void EnsureTrailingNewLine(JsonArray ops)
    {
        for (int index = ops.Count - 1; index >= 0; index--)
        {
            if (ops[index] is JsonObject op && TryGetStringInsert(op, out string text))
            {
                if (!text.EndsWith('\n'))
                {
                    op["insert"] = text + "\n";
                }

                return;
            }
        }

        ops.Add(new JsonObject { ["insert"] = "\n" });
    }
}
