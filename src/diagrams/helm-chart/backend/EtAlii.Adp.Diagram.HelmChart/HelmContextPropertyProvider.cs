using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// What the property grid shows for a selected chart element - everything real, and none of it
/// editable (Requirement 9.3).
/// </summary>
/// <remarks>
/// <para>
/// Every row carries a non-empty <see cref="ContextPropertyDefinition.ReadOnlyReason"/> naming
/// the file the value actually lives in - the "Defined in X; edit it in a text editor."
/// convention the first read-only provider established. Read-only is enforced rather than
/// styled: <c>ContextPropertyResolver</c> refuses a write to a read-only property server-side,
/// and <see cref="SetAsync"/> refuses anyway, because a provider that would silently accept a
/// write if the resolver ever changed is a trap rather than a design.
/// </para>
/// <para>
/// The chart node's grid carries the whole-chart summary (name, version, type, apiVersion and
/// the counts) - Requirement 9.4's nothing-selected summary rides the chart node, the one
/// element that means "this chart".
/// </para>
/// </remarks>
public sealed class HelmContextPropertyProvider : IContextPropertyProvider
{
    private const string Refusal =
        "A helm chart diagram shows the folder as it is and changes nothing in it. " +
        "Edit the file this value comes from in a text editor.";

    private readonly IHelmChartStore _store;

    public HelmContextPropertyProvider(IHelmChartStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.ElementId.Length == 0 || !Directory.Exists(target.ResolvedFullPath))
        {
            return Empty;
        }

        var chart = _store.GetOrLoad(target.ResolvedFullPath);
        var graph = HelmGraph.Derive(chart);

        if (graph.Node(target.ElementId) is { } node)
        {
            return ValueTask.FromResult(Describe(chart, graph, node));
        }

        var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, target.ElementId, StringComparison.Ordinal));
        return edge is null ? Empty : ValueTask.FromResult(Describe(chart, edge));
    }

    /// <summary>Refuses, always - a refusal, never an exception.</summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(Refusal));

    private static IReadOnlyList<ContextPropertyDefinition> Describe(HelmChart chart, HelmGraph graph, HelmNode node) =>
        node.Kind switch
        {
            HelmNodeKind.Chart => ChartRows(chart, node),
            HelmNodeKind.Values => ValuesRows(chart, node),
            HelmNodeKind.Template => TemplateRows(chart, node),
            HelmNodeKind.Dependency => DependencyRows(chart, graph, node),
            HelmNodeKind.Subchart or HelmNodeKind.Archive => VendoredRows(chart, graph, node),
            HelmNodeKind.Lock => LockRows(chart),
            HelmNodeKind.Schema => [Row("path", "Path", node.RelativePath, node.RelativePath, "Identity")],
            HelmNodeKind.Crds => CrdsRows(chart),
            _ => [],
        };

    /// <summary>The chart node: identity plus the whole-chart summary (Requirement 9.4).</summary>
    private static IReadOnlyList<ContextPropertyDefinition> ChartRows(HelmChart chart, HelmNode node)
    {
        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Name", node.Name, "Chart.yaml", "Identity"),
        };

        if (chart.Metadata is { } metadata)
        {
            Add(rows, "version", "Version", metadata.Version, "Chart.yaml", "Identity");
            Add(rows, "app-version", "App version", metadata.AppVersion, "Chart.yaml", "Identity");
            Add(rows, "api-version", "API version", metadata.ApiVersion, "Chart.yaml", "Identity");
            rows.Add(Row("type", "Type", metadata.ChartType, "Chart.yaml", "Identity"));
            Add(rows, "description", "Description", metadata.Description, "Chart.yaml", "Identity");
            if (metadata.Deprecated)
            {
                rows.Add(Row("deprecated", "Deprecated", "yes - the upstream marked it so", "Chart.yaml", "Identity"));
            }
        }

        if (chart.Legacy)
        {
            rows.Add(Row("legacy", "Legacy", "yes - a Helm 2 chart (apiVersion v1)", "Chart.yaml", "Identity"));
        }

        if (chart.MetadataFailure is { } failure)
        {
            rows.Add(Row("unreadable", "Unreadable", $"{failure.Message} (line {failure.Line})", "Chart.yaml", "Identity"));
        }

        rows.Add(Row("templates", "Templates", chart.Templates.Count.ToString(), "templates/", "Summary"));
        rows.Add(Row("dependencies", "Dependencies", chart.Dependencies.Count.ToString(), DeclaringFile(chart), "Summary"));
        rows.Add(Row("values-layers", "Values layers", chart.Values.Count.ToString(), "values.yaml", "Summary"));

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> ValuesRows(HelmChart chart, HelmNode node)
    {
        var values = chart.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        var rows = new List<ContextPropertyDefinition>
        {
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (values is null)
        {
            return rows;
        }

        rows.Add(Row(
            "role", "Role",
            values.IsDefault ? "default values" : "override layer, stacked on values.yaml",
            node.RelativePath, "Identity"));
        rows.Add(Row("keys", "Top-level keys", values.TopLevelKeys.Count.ToString(), node.RelativePath, "Contents"));
        if (values.HasGlobal)
        {
            rows.Add(Row("global", "Global", "yes - visible to every subchart", node.RelativePath, "Contents"));
        }

        if (values.Failure is { } failure)
        {
            rows.Add(Row("unreadable", "Unreadable", $"{failure.Message} (line {failure.Line})", node.RelativePath, "Identity"));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> TemplateRows(HelmChart chart, HelmNode node)
    {
        var template = chart.Templates.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        var rows = new List<ContextPropertyDefinition>
        {
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (template is null)
        {
            return rows;
        }

        rows.Add(Row("role", "Role", RoleLabel(template.Role), node.RelativePath, "Identity"));
        if (template.Facts.Kinds.Count > 0)
        {
            rows.Add(Row("kinds", "Renders", string.Join(", ", template.Facts.Kinds), node.RelativePath, "Contents"));
        }
        else if (template.Role == TemplateRole.Manifest)
        {
            rows.Add(Row(
                "kinds", "Renders",
                "undetermined - the kind is templated, and templates are never rendered here",
                node.RelativePath, "Contents"));
        }

        if (template.Facts.Defines.Count > 0)
        {
            rows.Add(Row("defines", "Defines", string.Join(", ", template.Facts.Defines), node.RelativePath, "Contents"));
        }

        if (template.Facts.References.Count > 0)
        {
            rows.Add(Row("includes", "Includes", string.Join(", ", template.Facts.References), node.RelativePath, "Contents"));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> DependencyRows(HelmChart chart, HelmGraph graph, HelmNode node)
    {
        var resolved = graph.Resolution.Dependencies.FirstOrDefault(candidate =>
            string.Equals(HelmGraph.DependencyId(candidate.Dependency), node.Id, StringComparison.Ordinal));
        if (resolved is null)
        {
            return [];
        }

        var declaringFile = DeclaringFile(chart);
        var dependency = resolved.Dependency;
        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Chart", dependency.Name, declaringFile, "Identity"),
        };

        Add(rows, "alias", "Alias", dependency.Alias, declaringFile, "Identity");
        Add(rows, "constraint", "Version constraint", dependency.VersionConstraint, declaringFile, "Identity");
        Add(rows, "repository", "Repository", dependency.Repository, declaringFile, "Identity");
        if (dependency.Condition is { Length: > 0 } condition)
        {
            rows.Add(Row("condition", "Condition", $"{condition} ({StateLabel(dependency.State)})", declaringFile, "Wiring"));
        }

        var pinned = chart.Lock?.Entries
            .FirstOrDefault(entry => string.Equals(entry.Name, dependency.Name, StringComparison.Ordinal))
            ?.Version;
        if (pinned is { Length: > 0 } && chart.Lock is { } chartLock)
        {
            rows.Add(Row("pinned", "Pinned", pinned, chartLock.RelativePath, "Wiring"));
        }

        rows.Add(resolved.Vendored is { } vendored
            ? Row("resolution", "Resolution", $"resolved - vendored at {vendored.RelativePath}", declaringFile, "Wiring")
            : Row(
                "resolution", "Resolution",
                "unvendored - nothing in charts/ answers it; helm dependency build fetches it at deploy time",
                declaringFile, "Wiring"));

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> VendoredRows(HelmChart chart, HelmGraph graph, HelmNode node)
    {
        var entry = chart.Vendored.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        if (entry is null)
        {
            return [];
        }

        var source = entry.Sealed ? entry.RelativePath : $"{entry.RelativePath}/Chart.yaml";
        var rows = new List<ContextPropertyDefinition>
        {
            Row("path", "Path", entry.RelativePath, source, "Identity"),
        };

        if (entry.Sealed)
        {
            rows.Add(Row(
                "sealed", "Sealed",
                "yes - an archive is labeled by its file name and never unpacked; there is no file to open",
                source, "Identity"));
        }
        else
        {
            Add(rows, "name", "Chart", entry.ChartName, source, "Identity");
            Add(rows, "version", "Version", entry.ChartVersion, source, "Identity");
            rows.Add(Row("type", "Type", entry.ChartType, source, "Identity"));
            rows.Add(Row("templates", "Templates", entry.TemplateCount.ToString(), source, "Contents"));
            if (entry.DeeperCount > 0)
            {
                rows.Add(Row(
                    "deeper", "Deeper charts/",
                    $"{entry.DeeperCount} entries - summarized, never recursed",
                    source, "Contents"));
            }
        }

        var declared = graph.Resolution.Dependencies.Any(resolved => ReferenceEquals(resolved.Vendored, entry));
        rows.Add(Row(
            "declared", "Declared",
            declared ? "yes" : "no - nothing declares this; validation warns about it",
            DeclaringFile(chart), "Wiring"));

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> LockRows(HelmChart chart)
    {
        if (chart.Lock is not { } chartLock)
        {
            return [];
        }

        var rows = new List<ContextPropertyDefinition>
        {
            Row("path", "Path", chartLock.RelativePath, chartLock.RelativePath, "Identity"),
            Row("entries", "Pinned dependencies", chartLock.Entries.Count.ToString(), chartLock.RelativePath, "Contents"),
        };

        if (chartLock.Failure is { } failure)
        {
            rows.Add(Row("unreadable", "Unreadable", $"{failure.Message} (line {failure.Line})", chartLock.RelativePath, "Identity"));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> CrdsRows(HelmChart chart) =>
        chart.Crds is { } crds
            ?
            [
                Row("path", "Path", "crds/", "crds/", "Identity"),
                Row("files", "Files", crds.FileCount.ToString(), "crds/", "Contents"),
            ]
            : [];

    private static IReadOnlyList<ContextPropertyDefinition> Describe(HelmChart chart, HelmEdge edge)
    {
        var declaringFile = DeclaringFile(chart);
        var rows = new List<ContextPropertyDefinition>
        {
            Row("kind", "Relationship", Verb(edge.Kind), declaringFile, "Identity"),
        };

        Add(rows, "label", "Says", edge.Label, declaringFile, "Identity");
        if (edge.OpenEnd)
        {
            rows.Add(Row(
                "open-end", "Open end",
                edge.Kind == HelmEdgeKind.Resolves
                    ? "nothing vendored answers this dependency; there is nothing to open"
                    : "no local partial defines this name - a library chart provides it at render time",
                declaringFile, "Identity"));
        }

        return rows;
    }

    /// <summary>
    /// The one way a row is made, and the reason every one of them carries: there is no path
    /// through this class that can produce a writable property, because there is no other way
    /// to make one.
    /// </summary>
    private static ContextPropertyDefinition Row(string id, string label, string value, string source, string group) =>
        new(
            $"helm.{id}",
            label,
            value,
            ContextPropertyEditor.Line,
            $"Defined in {source}; edit it in a text editor.",
            group);

    /// <summary>Absent rather than empty when there is nothing to say.</summary>
    private static void Add(List<ContextPropertyDefinition> rows, string id, string label, string? value, string source, string group)
    {
        if (value is { Length: > 0 })
        {
            rows.Add(Row(id, label, value, source, group));
        }
    }

    private static string DeclaringFile(HelmChart chart) => chart.Legacy ? "requirements.yaml" : "Chart.yaml";

    private static string RoleLabel(TemplateRole role) => role switch
    {
        TemplateRole.Manifest => "manifest template",
        TemplateRole.Partial => "partial - defines named templates, renders nothing itself",
        TemplateRole.Notes => "install notes",
        TemplateRole.Test => "helm test hook",
        _ => "",
    };

    private static string StateLabel(ConditionState state) => state switch
    {
        ConditionState.On => "currently on",
        ConditionState.Off => "currently off",
        ConditionState.Missing => "the path is not in values.yaml",
        ConditionState.Unknown => "not resolvable to a boolean",
        _ => "",
    };

    private static string Verb(HelmEdgeKind kind) => kind switch
    {
        HelmEdgeKind.Declares => "declares",
        HelmEdgeKind.Resolves => "resolves to",
        HelmEdgeKind.Overrides => "overrides",
        HelmEdgeKind.Configures => "configures",
        HelmEdgeKind.Includes => "includes",
        _ => "",
    };

    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
}
