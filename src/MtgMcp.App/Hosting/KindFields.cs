using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Builds the failures shared by the tools whose required <c>kind</c> field picks the job and whose other fields
/// apply to only some kinds. Every such tool reports the same reason codes and names the field in <c>path</c>.
/// </summary>
internal static class KindFields
{
    /// <summary>
    /// Names the reason used when a field does not belong to the chosen kind.
    /// </summary>
    internal const string FieldNotAllowed = "field-not-allowed";

    /// <summary>
    /// Names the reason used when a field the chosen kind needs is missing.
    /// </summary>
    internal const string MissingField = "missing-field";

    /// <summary>
    /// Names the reason used when the kind itself is not one of the allowed values.
    /// </summary>
    internal const string InvalidKind = "invalid-kind";

    /// <summary>
    /// Refuses a kind that is not in the allowed list.
    /// </summary>
    internal static OperationInvalidInput? CheckKind(string? kind, params string[] allowed)
    {
        return kind is not null && allowed.Contains(kind, StringComparer.Ordinal)
            ? null
            : new OperationInvalidInput(
                InvalidKind,
                "kind must be " + string.Join(", ", allowed[..^1]) + (allowed.Length > 2 ? ", or " : " or ")
                    + allowed[^1] + ".",
                "kind");
    }

    /// <summary>
    /// Refuses a missing text field that the chosen kind requires.
    /// </summary>
    internal static OperationInvalidInput? RequireText(string kind, string field, string? value)
    {
        return Require(kind, field, !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Refuses a field the chosen kind requires when it was not given.
    /// </summary>
    internal static OperationInvalidInput? Require(string kind, string field, bool given)
    {
        return given
            ? null
            : new OperationInvalidInput(MissingField, $"{field} is required when kind is {kind}.", field);
    }

    /// <summary>
    /// Refuses the first field that was given although the chosen kind does not use it.
    /// </summary>
    internal static OperationInvalidInput? RejectGiven(string kind, params (string Name, bool Given)[] fields)
    {
        foreach ((string name, bool given) in fields)
        {
            if (given)
            {
                return new OperationInvalidInput(
                    FieldNotAllowed,
                    $"{name} is not allowed when kind is {kind}.",
                    name);
            }
        }

        return null;
    }
}
