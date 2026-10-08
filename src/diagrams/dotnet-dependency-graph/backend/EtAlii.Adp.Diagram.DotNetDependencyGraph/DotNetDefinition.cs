using System.Runtime.CompilerServices;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The .NET dependency graph's bundled DISL definition (<c>definition/dotnet-dependency-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what is derived from it: the property rows of a project or a
/// package, and the sentence a write is refused with, mapped onto the host's ids by its <c>x-dotnet</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is built from what the readers read</b>, not through FBL: a graph spans the solution, every
/// project file it names, the <c>Directory.Packages.props</c> files above them and the local NuGet cache,
/// where an FBL binding reads one file. <see cref="ModelOf"/> turns one <see cref="DependencyGraphModel"/>
/// into a <see cref="DislDiagram"/> once, element for element and with the graph's own ids, and keeps it
/// for as long as the graph lives, so every selection of one reading reads one model.
/// </para>
/// <para>
/// <b>The summaries are the readers'</b>: a package's versions, conflict, dependent count and ambient
/// marking and a project's .NET version are computed by <see cref="DependencyGraph"/> and stored on the
/// model as the read-only attributes the definition declares them to be, never recomputed in CEL.
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): every row carries the reason its form item gives,
/// and a write is refused with the definition's <c>std.readOnly</c>.
/// </para>
/// </remarks>
internal static class DotNetDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(DotNetDefinition).Assembly, "dotnet-dependency-graph.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-dotnet"));

    private static readonly Lazy<string> LoadedRefusal = new(RefusalOf);

    private static readonly ConditionalWeakTable<DependencyGraphModel, DislDiagram> Models = [];

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    private static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-dotnet</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing here can be written: the definition's <c>std.readOnly</c>.</summary>
    public static string Refusal => LoadedRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="graph"/>: its form's; none for a reference or an id the graph does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DependencyGraphModel graph, string elementId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(elementId);
        if (ModelOf(graph).ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary><paramref name="graph"/> as the definition's model: a node per project and package, a relation per reference.</summary>
    private static DislDiagram ModelOf(DependencyGraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Models.GetValue(graph, Build);
    }

    private static DislDiagram Build(DependencyGraphModel graph)
    {
        var diagram = new DislDiagram(Specification);
        var nodes = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var project in graph.Projects)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = project.Name,
                ["path"] = project.RelativePath,
                ["targetFrameworks"] = project.TargetFrameworks.Cast<object?>().ToList(),
            };
            if (project.DotNetVersion is { } version) attributes["dotNetVersion"] = version;
            nodes.TryAdd(project.Id, diagram.AddNode("Project", project.Id, attributes));
        }

        foreach (var package in graph.Packages)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = package.PackageId,
                ["versions"] = package.Versions.Cast<object?>().ToList(),
                ["hasVersionConflict"] = package.HasVersionConflict,
                ["dependentProjectCount"] = (long)package.DependentProjectCount,
                ["isAmbient"] = package.IsAmbient,
            };
            if (package.Description is { } description) attributes["description"] = description;
            nodes.TryAdd(package.Id, diagram.AddNode("Package", package.Id, attributes));
        }

        // A reference whose end the graph does not hold - a package id spelled in another casing - is
        // kept with that end missing, as the definition's model can have it; the canvas draws neither.
        foreach (var edge in graph.Edges)
        {
            diagram.AddRelation(
                edge.Kind == DependsOnKind.Project ? "ProjectReference" : "PackageReference",
                edge.Id,
                nodes.GetValueOrDefault(edge.FromElementId),
                nodes.GetValueOrDefault(edge.ToElementId));
        }

        return diagram;
    }

    /// <summary>The sentence of <c>behavior.messages</c>' <c>std.readOnly</c>, as the definition writes it.</summary>
    private static string RefusalOf() =>
        Specification.Root.GetProperty("behavior").GetProperty("messages").GetProperty("std.readOnly") is { ValueKind: JsonValueKind.String } message
            ? message.GetString()!
            : throw new InvalidOperationException("The definition words no std.readOnly to refuse a write with.");

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);
}
