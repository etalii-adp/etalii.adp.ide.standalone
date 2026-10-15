using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// Today's reading of a chart folder as the definition's model: one node per node of
/// <see cref="HelmGraph"/> and one relation per edge, in the graph's order and with the graph's ids,
/// each carrying the attributes the definition declares for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for the binding, until the folder is read through it</b> (helm-chart-disl-fbl, task 9,
/// which deletes this class): derivation changes in one step and reading in another, so a difference
/// in the transcript names one of them. Until then <see cref="HelmChartReader"/> reads the folder and
/// this hands what it read to the definition, as the plugin will.
/// </para>
/// <para>
/// <b>All five relations are stored ones here</b>, because the graph already made them. Task 6 lets
/// DISL derive <c>Declares</c>, <c>Overrides</c> and <c>Configures</c>.
/// </para>
/// <para>
/// <b>A relation finds its ends by id</b>, the first node of that id in model order, as a model read
/// from a file finds them; two dependencies of one effective name share one id today (finding A10).
/// </para>
/// </remarks>
internal static class HelmDisl
{
    public static DislDiagram Build(DislSpecification specification, HelmChart chart, HelmGraph graph)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(graph);

        var diagram = new DislDiagram(specification);
        var nodes = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        using var dependencies = graph.Resolution.Dependencies.Select(resolved => resolved.Dependency).GetEnumerator();

        foreach (var node in graph.Nodes)
        {
            (string type, Dictionary<string, object?> attributes) = node.Kind switch
            {
                HelmNodeKind.Chart => ("Chart", Chart(chart, node)),
                HelmNodeKind.Values => ("Values", Values(chart, node)),
                HelmNodeKind.Schema => ("Schema", Path(node)),
                HelmNodeKind.Template => Template(chart, node),
                HelmNodeKind.Crds => ("Crds", Crds(chart, node)),
                HelmNodeKind.Dependency => ("Dependency", Dependency(dependencies.MoveNext() ? dependencies.Current : null)),
                HelmNodeKind.Subchart => ("Subchart", Vendored(chart, node)),
                HelmNodeKind.Archive => ("Archive", Vendored(chart, node)),
                HelmNodeKind.Lock => ("Lock", Lock(chart, node)),
                _ => throw new InvalidOperationException($"A node of kind {node.Kind} has no type in the definition."),
            };
            nodes.TryAdd(node.Id, diagram.AddNode(type, node.Id, attributes));
        }

        foreach (var edge in graph.Edges)
        {
            diagram.AddRelation(
                edge.Kind.ToString(),
                edge.Id,
                nodes.GetValueOrDefault(edge.SourceId),
                edge.OpenEnd ? null : nodes.GetValueOrDefault(edge.TargetId),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = edge.Label });
        }

        return diagram;
    }

    private static Dictionary<string, object?> Chart(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Metadata is { } metadata)
        {
            Set(attributes, "name", metadata.Name);
            Set(attributes, "version", metadata.Version);
            attributes["versionLine"] = (long)metadata.VersionLine;
            Set(attributes, "appVersion", metadata.AppVersion);
            Set(attributes, "apiVersion", metadata.ApiVersion);
            attributes["chartType"] = metadata.ChartType;
            attributes["description"] = metadata.Description;
            attributes["deprecated"] = metadata.Deprecated;
        }

        Set(attributes, "failure", Failure(chart.MetadataFailure));
        attributes["templateCount"] = (long)chart.Templates.Count;
        attributes["dependencyCount"] = (long)chart.Dependencies.Count;
        attributes["valuesLayerCount"] = (long)chart.Values.Count;
        return attributes;
    }

    private static Dictionary<string, object?> Values(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Values.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal)) is { } values)
        {
            attributes["isDefault"] = values.IsDefault;
            attributes["keys"] = Strings(values.TopLevelKeys);
            Set(attributes, "failure", Failure(values.Failure));
        }

        return attributes;
    }

    /// <summary>A partial is its own type, so that it travels as its own wire type; every other role is a <c>Template</c>.</summary>
    private static (string Type, Dictionary<string, object?> Attributes) Template(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Templates.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal)) is not { } template)
        {
            return ("Template", attributes);
        }

        attributes["kinds"] = Strings(template.Facts.Kinds);
        attributes["undetermined"] = template is { Role: TemplateRole.Manifest, Facts.Kinds.Count: 0 };
        attributes["defines"] = Strings(template.Facts.Defines);
        attributes["references"] = Strings(template.Facts.References);
        return (template.Role == TemplateRole.Partial ? "Partial" : "Template", attributes);
    }

    private static Dictionary<string, object?> Crds(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Crds is { } crds)
        {
            attributes["files"] = (long)crds.FileCount;
            attributes["failures"] = crds.Failures.Select(failure => (object?)Failure(failure)).ToList();
        }

        return attributes;
    }

    private static Dictionary<string, object?> Dependency(DependencyDeclaration? dependency)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (dependency is null)
        {
            return attributes;
        }

        attributes["name"] = dependency.Name;
        Set(attributes, "alias", dependency.Alias);
        Set(attributes, "constraint", dependency.VersionConstraint);
        Set(attributes, "repository", dependency.Repository);
        Set(attributes, "condition", dependency.Condition);
        attributes["conditionState"] = dependency.State switch
        {
            ConditionState.On => "on",
            ConditionState.Off => "off",
            ConditionState.Missing => "missing",
            ConditionState.Unknown => "unknown",
            _ => "none",
        };
        return attributes;
    }

    private static Dictionary<string, object?> Vendored(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Vendored.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal)) is not { } entry)
        {
            return attributes;
        }

        attributes["entryName"] = entry.EntryName;
        Set(attributes, "chartName", entry.ChartName);
        Set(attributes, "chartVersion", entry.ChartVersion);
        attributes["chartType"] = entry.ChartType;
        Set(attributes, "failure", Failure(entry.Failure));
        if (!entry.Sealed)
        {
            attributes["templates"] = (long)entry.TemplateCount;
            attributes["deeper"] = (long)entry.DeeperCount;
        }

        return attributes;
    }

    private static Dictionary<string, object?> Lock(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Lock is { } chartLock)
        {
            attributes["entries"] = chartLock.Entries
                .Select(entry => (object?)new CelMap { ["name"] = entry.Name, ["version"] = entry.Version ?? "", ["line"] = (long)entry.Line })
                .ToList();
            Set(attributes, "failure", Failure(chartLock.Failure));
        }

        return attributes;
    }

    private static Dictionary<string, object?> Path(HelmNode node) =>
        new(StringComparer.Ordinal) { ["path"] = node.RelativePath };

    /// <summary>A file that did not parse, as the definition's <c>YamlFailure</c>; null for one that did.</summary>
    private static CelMap? Failure(TolerantYamlFailure? failure) =>
        failure is null
            ? null
            : new CelMap { ["path"] = failure.RelativePath, ["message"] = failure.Message, ["line"] = (long)failure.Line };

    private static List<object?> Strings(IEnumerable<string> values) => [.. values];

    /// <summary>Stores what was read and leaves unset what was not, so the attribute reads as its default.</summary>
    private static void Set(Dictionary<string, object?> attributes, string name, object? value)
    {
        if (value is not null)
        {
            attributes[name] = value;
        }
    }
}
