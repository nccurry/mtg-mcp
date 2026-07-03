using System.ComponentModel;
using System.Reflection;
using System.Text;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.App.Tests;

/// <summary>
/// Reports and verifies the public MCP surface inventory used by release planning.
/// </summary>
public sealed class McpSurfaceInventoryTests
{
    /// <summary>
    /// Lists all tool wrapper types that contribute to the MCP surface.
    /// </summary>
    private static readonly Type[] ToolTypes =
    [
        typeof(CardTools),
        typeof(CollectionTools),
        typeof(WorkspaceTools),
        typeof(DeckMutationTools),
        typeof(CategoryTools),
        typeof(CheckpointTools),
        typeof(AnalysisTools),
        typeof(DeckReEvaluationTools),
        typeof(RecommendationTools),
        typeof(CorpusTools),
        typeof(PlanTools),
        typeof(SimulationTools),
        typeof(IntentTools),
        typeof(FacetTools),
        typeof(PlaygroupTools),
        typeof(ServerTools),
    ];

    /// <summary>
    /// Lists all resource wrapper types that contribute to the MCP surface.
    /// </summary>
    private static readonly Type[] ResourceTypes = [typeof(MtgResources)];

    /// <summary>
    /// Lists all prompt wrapper types that contribute to the MCP surface.
    /// </summary>
    private static readonly Type[] PromptTypes = [typeof(MtgPrompts)];

    /// <summary>
    /// Emits report-only surface metrics so CI has a visible baseline before ratcheting.
    /// </summary>
    [Fact]
    public void SurfaceMetrics_ReportCurrentInventory()
    {
        Dictionary<string, string?> toolTitles = ToolRegistry
            .CreateTools(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })
            .ToDictionary(tool => tool.ProtocolTool.Name, tool => tool.ProtocolTool.Title, StringComparer.Ordinal);
        SurfaceMember[] tools = ToolTypes
            .SelectMany(type => GetSurfaceMembers(type, "McpServerToolAttribute", "Name"))
            .Select(member => member with
            {
                Title = toolTitles.TryGetValue(member.Name, out string? title)
                    ? title
                    : member.Title
            })
            .ToArray();
        SurfaceMember[] resources = ResourceTypes
            .SelectMany(type => GetSurfaceMembers(type, "McpServerResourceAttribute", "UriTemplate"))
            .ToArray();
        SurfaceMember[] prompts = PromptTypes
            .SelectMany(type => GetSurfaceMembers(type, "McpServerPromptAttribute", "Name"))
            .ToArray();

        tools.Should().NotBeEmpty();
        resources.Should().NotBeEmpty();
        prompts.Should().NotBeEmpty();

        StringBuilder report = new();
        report.AppendLine("MCP surface report (report-only baseline)");
        report.AppendLine(FormattableString.Invariant($"Tools: {tools.Length}"));
        report.AppendLine(FormattableString.Invariant($"Resources: {resources.Length}"));
        report.AppendLine(FormattableString.Invariant($"Prompts: {prompts.Length}"));
        report.AppendLine(FormattableString.Invariant(
            $"Tool title coverage: {tools.Count(tool => !string.IsNullOrWhiteSpace(tool.Title))}/{tools.Length}"));
        report.AppendLine(FormattableString.Invariant(
            $"Tool description coverage: {tools.Count(tool => !string.IsNullOrWhiteSpace(tool.Description))}/{tools.Length}"));
        report.AppendLine(FormattableString.Invariant(
            $"Longest rough tool schema estimate: {tools.Max(tool => tool.RoughSchemaTokens)} tokens"));
        report.AppendLine("Largest rough tool schemas:");

        foreach (SurfaceMember tool in tools.OrderByDescending(tool => tool.RoughSchemaTokens).Take(10))
        {
            report.AppendLine(FormattableString.Invariant(
                $"{tool.Name}: params={tool.ParameterCount}, roughSchemaTokens={tool.RoughSchemaTokens}, title={(string.IsNullOrWhiteSpace(tool.Title) ? "missing" : "present")}"));
        }

        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        File.WriteAllText(Path.Combine(artifactsDirectory, "surface-report.txt"), report.ToString());

        Console.WriteLine(report.ToString());
    }

    /// <summary>
    /// Verifies README.md names every registered public tool, resource, and prompt.
    /// </summary>
    [Fact]
    public void ReadmeSurfaceInventory_CoversRegisteredSurface()
    {
        string readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));
        string[] names = ToolTypes
            .SelectMany(type => GetSurfaceMembers(type, "McpServerToolAttribute", "Name"))
            .Concat(ResourceTypes.SelectMany(type => GetSurfaceMembers(type, "McpServerResourceAttribute", "UriTemplate")))
            .Concat(PromptTypes.SelectMany(type => GetSurfaceMembers(type, "McpServerPromptAttribute", "Name")))
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        List<string> missing = [];
        foreach (string name in names)
        {
            if (!readme.Contains($"`{name}`", StringComparison.Ordinal))
            {
                missing.Add(name);
            }
        }

        missing.Should().BeEmpty("README.md should enumerate every registered MCP tool, resource, and prompt");
    }

    /// <summary>
    /// Reads public surface members from MCP attributes.
    /// </summary>
    private static IEnumerable<SurfaceMember> GetSurfaceMembers(Type type, string attributeName, string nameProperty)
    {
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            CustomAttributeData? attribute = method.CustomAttributes.FirstOrDefault(item =>
                item.AttributeType.Name == attributeName);
            if (attribute is null)
            {
                continue;
            }

            string? name = GetAttributeString(attribute, nameProperty);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            int parameterCount = method.GetParameters()
                .Count(parameter => parameter.ParameterType != typeof(CancellationToken));
            int roughSchemaTokens = Math.Max(
                1,
                (name.Length + description.Length + method.Name.Length) / 4 + (parameterCount * 20));

            yield return new SurfaceMember(
                name,
                GetAttributeString(attribute, "Title"),
                description,
                parameterCount,
                roughSchemaTokens);
        }
    }

    /// <summary>
    /// Reads constructor or named attribute string values.
    /// </summary>
    private static string? GetAttributeString(CustomAttributeData attribute, string propertyName)
    {
        foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
        {
            if (argument.MemberName == propertyName && argument.TypedValue.Value is string value)
            {
                return value;
            }
        }

        if (attribute.ConstructorArguments.Count > 0
            && propertyName is "Name" or "UriTemplate"
            && attribute.ConstructorArguments[0].Value is string constructorValue)
        {
            return constructorValue;
        }

        return null;
    }

    /// <summary>
    /// Finds the repository root from the test working directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    /// <summary>
    /// Captures reportable public surface metadata.
    /// </summary>
    private sealed record SurfaceMember(
        string Name,
        string? Title,
        string Description,
        int ParameterCount,
        int RoughSchemaTokens);
}
