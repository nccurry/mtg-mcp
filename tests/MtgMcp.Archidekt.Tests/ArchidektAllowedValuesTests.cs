using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Verifies every value the Archidekt request schemas publish is one the adapter accepts at run time.
/// </summary>
public sealed class ArchidektAllowedValuesTests
{
    /// <summary>
    /// Verifies every published deck format maps to a provider format ID.
    /// </summary>
    [Fact]
    public void DeckFormat_EveryPublishedValue_MapsToProviderId()
    {
        string[] values = Published(typeof(ArchidektDeckCreateRequest), nameof(ArchidektDeckCreateRequest.Format));

        Assert.NotEmpty(values);
        foreach (string value in values)
        {
            Assert.True(ArchidektDeckTransport.MapFormatId(value) > 0, value);
        }
    }

    /// <summary>
    /// Verifies every published deck visibility is accepted.
    /// </summary>
    [Fact]
    public void DeckVisibility_EveryPublishedValue_IsAccepted()
    {
        string[] values = Published(typeof(ArchidektDeckCreateRequest), nameof(ArchidektDeckCreateRequest.Visibility));

        Assert.Equal(["private", "unlisted", "public"], values);
        foreach (string value in values)
        {
            Assert.Equal(value, ArchidektContract.NormalizeVisibility(value));
        }
    }

    /// <summary>
    /// Verifies the create and update requests publish the same folder visibility values, and all are accepted.
    /// </summary>
    [Fact]
    public void FolderVisibility_EveryPublishedValue_IsAccepted()
    {
        string[] create = Published(
            typeof(ArchidektFolderCreateRequest),
            nameof(ArchidektFolderCreateRequest.Visibility));
        string[] update = Published(
            typeof(ArchidektFolderUpdateRequest),
            nameof(ArchidektFolderUpdateRequest.Visibility));

        Assert.Equal(create, update);
        Assert.Equal(["private", "public"], create);
        foreach (string value in create)
        {
            Assert.Equal(value, ArchidektFolderOperations.NormalizeVisibility(value));
        }
    }

    /// <summary>
    /// Verifies every published folder move item kind is accepted.
    /// </summary>
    [Fact]
    public void FolderMoveKind_EveryPublishedValue_IsAccepted()
    {
        string[] values = Published(typeof(ArchidektFolderMoveItem), nameof(ArchidektFolderMoveItem.Kind));

        Assert.Equal(["deck", "folder"], values);
        foreach (string value in values)
        {
            ArchidektFolderMoveItem[] accepted = ArchidektFolderOperations.DeduplicateMoveItems(
                [new ArchidektFolderMoveItem(value, "1", null)]);

            Assert.Single(accepted);
        }
    }

    /// <summary>
    /// Verifies values outside the published lists are still rejected, so the acceptance checks can fail.
    /// </summary>
    [Fact]
    public void UnpublishedValues_AreRejected()
    {
        Assert.Throws<ArchidektProviderException>(() => ArchidektFolderOperations.NormalizeVisibility("unlisted"));
        Assert.Throws<ArchidektProviderException>(() => ArchidektDeckTransport.MapFormatId("custom"));
    }

    /// <summary>
    /// Reads the allowed values a request record property publishes in its schema.
    /// </summary>
    private static string[] Published(Type requestType, string propertyName)
    {
        PropertyInfo property = requestType.GetProperty(propertyName)!;
        AllowedValuesAttribute attribute = property.GetCustomAttribute<AllowedValuesAttribute>(inherit: true)!;
        return attribute.Values.Cast<string>().ToArray();
    }
}
