using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// Today's reading of a chart folder as the definition's model: one element per node of
/// <see cref="HelmGraph"/> and one relation per <c>Resolves</c> and <c>Includes</c> edge, in the graph's
/// order, each carrying the attributes the definition declares for it and the file and line it is
/// read at; the library then computes every id and derives <c>Declares</c>, <c>Overrides</c> and
/// <c>Configures</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for the binding, until the folder is read through it</b> (helm-chart-disl-fbl, task 9,
/// which deletes this class): derivation changes in one step and reading in another, so a difference
/// in the transcript names one of them. Until then <see cref="HelmChartReader"/> reads the folder and
/// this hands what it read to the definition as a reading, the shape the plugin will deliver, so that
/// <see cref="DislModelBuilder"/> builds the model as it will then.
/// </para>
/// <para>
/// <b>Where an element is read</b> is what a finding located at it reports (DISL 8.2): the chart is at
/// the start of <c>Chart.yaml</c>'s root mapping, a dependency at its entry in the declaring file, and a
/// file's node in that file without a line. No finding is located at a folder's node, so the
/// <c>crds/</c> folder and an unpacked subchart are given no file.
/// </para>
/// <para>
/// <b>A relation finds its ends by id</b>, the first node read with that id, as a model read from a
/// file finds them; two dependencies of one effective name share one id today (finding A10).
/// </para>
/// <para>
/// <b>One relation per id</b> (the ruling of 2026-10-16, Requirement 1.4, design error scenario 6).
/// Two dependencies of one effective name, both unvendored or both resolved to one entry, make one
/// <c>Resolves</c> twice, with one id and the same content. The library draws a derived relation once
/// per id and so drops the second <c>Declares</c> and <c>Configures</c> of such a pair; the second
/// <c>Resolves</c> is not handed to it either, so that it is not reported as a duplicate id.
/// </para>
/// </remarks>
internal static class HelmDisl
{
    public static DislDiagram Build(DislSpecification specification, HelmChart chart, HelmGraph graph)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(graph);

        var elements = new List<FblElement>();
        using var dependencies = graph.Resolution.Dependencies.Select(resolved => resolved.Dependency).GetEnumerator();

        foreach (var node in graph.Nodes)
        {
            (string type, Dictionary<string, object?> attributes, string? file, uint line) = node.Kind switch
            {
                HelmNodeKind.Chart => ("Chart", Chart(chart, node), "Chart.yaml", chart.Metadata?.Line ?? 0),
                HelmNodeKind.Values => ("Values", Values(chart, node), node.RelativePath, 0u),
                HelmNodeKind.Schema => ("Schema", Path(node), node.RelativePath, 0u),
                HelmNodeKind.Template => Template(chart, node),
                HelmNodeKind.Crds => ("Crds", Crds(chart, node), null, 0u),
                HelmNodeKind.Dependency => Dependency(chart, dependencies.MoveNext() ? dependencies.Current : null),
                HelmNodeKind.Subchart => ("Subchart", Vendored(chart, node), null, 0u),
                HelmNodeKind.Archive => ("Archive", Vendored(chart, node), node.RelativePath, 0u),
                HelmNodeKind.Lock => ("Lock", Lock(chart, node), node.RelativePath, 0u),
                _ => throw new InvalidOperationException($"A node of kind {node.Kind} has no type in the definition."),
            };
            elements.Add(Element(node.Id, type, attributes, null, null, file, line));
        }

        var relations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(edge => relations.Add(edge.Id)))
        {
            elements.Add(Element(
                edge.Id,
                edge.Kind.ToString(),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = edge.Label },
                edge.SourceId,
                edge.OpenEnd ? null : edge.TargetId,
                null,
                0));
        }

        return DislModelBuilder.From(new FblModel(elements, [], Unreadable: false), specification).Diagram;
    }

    /// <summary>One element of the reading: a node, or a relation when it has a source.</summary>
    private static FblElement Element(string id, string type, Dictionary<string, object?> attributes, string? source, string? target, string? file, uint line) =>
        new(id, IdIsStored: false, type, Rule: type, IsRelation: source is not null, attributes, ParentId: null, ParentSlot: null, source, target, default, (int)line)
        {
            File = file,
        };

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
    private static (string Type, Dictionary<string, object?> Attributes, string? File, uint Line) Template(HelmChart chart, HelmNode node)
    {
        var attributes = Path(node);
        if (chart.Templates.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal)) is not { } template)
        {
            return ("Template", attributes, node.RelativePath, 0);
        }

        attributes["kinds"] = Strings(template.Facts.Kinds);
        attributes["undetermined"] = template is { Role: TemplateRole.Manifest, Facts.Kinds.Count: 0 };
        attributes["defines"] = Strings(template.Facts.Defines);
        attributes["references"] = Strings(template.Facts.References);
        return (template.Role == TemplateRole.Partial ? "Partial" : "Template", attributes, node.RelativePath, 0);
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

    /// <summary>A dependency, read at its entry in the file that declares it: <c>requirements.yaml</c> for a legacy chart, <c>Chart.yaml</c> otherwise.</summary>
    private static (string Type, Dictionary<string, object?> Attributes, string? File, uint Line) Dependency(HelmChart chart, DependencyDeclaration? dependency)
    {
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        return ("Dependency", DependencyAttributes(dependency), declaringFile, dependency?.Line ?? 0);
    }

    private static Dictionary<string, object?> DependencyAttributes(DependencyDeclaration? dependency)
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
