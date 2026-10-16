using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The module's hand-written logic, kept verbatim as the oracle the derived answers are compared with, chart
/// for chart and element for element (helm-chart-disl-fbl, Requirement 5.8): the property rows and the
/// refusal as <c>HelmContextPropertyProvider</c> wrote them before they were derived from the DISL definition
/// (task 5), and the findings as <c>HelmRuleSet</c> judged them and the relations as <c>HelmGraph</c> made
/// them before the definition derived them (task 6).
/// </summary>
internal static class HandWrittenHelm
{
    /// <summary>The sentence <c>SetAsync</c> refused every write with.</summary>
    public const string Refusal =
        "A helm chart diagram shows the folder as it is and changes nothing in it. " +
        "Edit the file this value comes from in a text editor.";

    /// <summary>The rows of <paramref name="elementId"/> in <paramref name="chart"/>, as <c>DescribeAsync</c> answered them once it had the chart: none for the empty id and for an id the graph does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(HelmChart chart, string elementId)
    {
        if (elementId.Length == 0)
        {
            return [];
        }

        var graph = HelmGraph.Derive(chart);

        if (graph.Node(elementId) is { } node)
        {
            return Describe(chart, graph, node);
        }

        var edge = Edges(chart).FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        return edge is null ? [] : Describe(chart, edge);
    }

    /// <summary>
    /// The relations of <paramref name="chart"/> as <c>HelmGraph.Derive</c> made all five, in its order: each
    /// override onto the default values layer, each include, then per dependency its <c>Declares</c>, its
    /// <c>Resolves</c> and its <c>Configures</c>. Two dependencies of one effective name repeat the relations
    /// whose ids do not tell them apart, as they did.
    /// </summary>
    public static IReadOnlyList<HelmEdge> Edges(HelmChart chart)
    {
        var edges = new List<HelmEdge>();
        var resolution = DependencyResolution.Match(chart.Dependencies, chart.Vendored);
        if (!chart.IsChart)
        {
            return edges;
        }

        const string chartId = "chart";
        var defaultValues = chart.Values.FirstOrDefault(values => values.IsDefault);
        foreach (var values in chart.Values)
        {
            if (!values.IsDefault && defaultValues is not null)
            {
                edges.Add(new HelmEdge(ValuesId(values), ValuesId(defaultValues), HelmEdgeKind.Overrides, string.Empty, OpenEnd: false));
            }
        }

        var partials = chart.Templates.Where(template => template.Role == TemplateRole.Partial).ToArray();
        foreach (var template in chart.Templates)
        {
            foreach (var reference in template.Facts.References)
            {
                var defining = partials.FirstOrDefault(partial =>
                    partial.Facts.Defines.Contains(reference, StringComparer.Ordinal));
                edges.Add(defining is not null
                    ? new HelmEdge(TemplateId(template), TemplateId(defining), HelmEdgeKind.Includes, reference, OpenEnd: false)
                    : new HelmEdge(TemplateId(template), string.Empty, HelmEdgeKind.Includes, reference, OpenEnd: true));
            }
        }

        foreach (var resolved in resolution.Dependencies)
        {
            var dependency = resolved.Dependency;
            var id = HelmGraph.DependencyId(dependency);
            edges.Add(new HelmEdge(chartId, id, HelmEdgeKind.Declares, dependency.VersionConstraint ?? string.Empty, OpenEnd: false));
            edges.Add(resolved.Vendored is { } vendored
                ? new HelmEdge(id, VendoredId(vendored), HelmEdgeKind.Resolves, string.Empty, OpenEnd: false)
                : new HelmEdge(id, string.Empty, HelmEdgeKind.Resolves, dependency.EffectiveName, OpenEnd: true));

            if (defaultValues is not null
                && defaultValues.TopLevelKeys.Contains(dependency.EffectiveName, StringComparer.Ordinal))
            {
                edges.Add(new HelmEdge(
                    ValuesId(defaultValues), id, HelmEdgeKind.Configures, dependency.EffectiveName, OpenEnd: false));
            }
        }

        return edges;

        static string ValuesId(ValuesFile values) => $"values:{values.RelativePath}";

        static string TemplateId(TemplateFile template) => $"tpl:{template.RelativePath}";

        static string VendoredId(VendoredEntry entry) => $"{(entry.Sealed ? "tgz" : "sub")}:{entry.RelativePath}";
    }

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

    // ---- The findings, as HelmRuleSet.Judge gave them (task 6) ---------------------------------------
    //
    // The noise guards are part of the specification. An Unvendored dependency is never a finding, a
    // missing lock is never a finding either, and library charts escape the empty-templates rule. The
    // panel knew two severities, so the legacy finding is a Warning worded as information.

    /// <summary>MAJOR.MINOR.PATCH with optional pre-release and build tails - SemVer's shape, not its algebra.</summary>
    private static readonly Regex _semVer = new(
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<DiagramProblem> Findings(HelmChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        if (!chart.IsChart)
        {
            // Exactly one finding, attributed to the folder itself: a registered non-chart is
            // one fact, not a cascade (R10.1). Core's ProblemStamp keeps a folder-located
            // problem fresh - the seam built for exactly this kind of rule.
            return
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    "This folder is not a Helm chart: it has no Chart.yaml.",
                    HelmRules.NotAChart,
                    new DiagramProblemFileLocation(".")),
            ];
        }

        var problems = new List<DiagramProblem>();
        problems.AddRange(UnreadableFiles(chart));
        problems.AddRange(Metadata(chart));
        problems.AddRange(Vendoring(chart));
        problems.AddRange(LockDrift(chart));
        problems.AddRange(Templates(chart));
        problems.AddRange(Conditions(chart));
        problems.AddRange(Collisions(chart));

        return problems;
    }

    /// <summary>Chart-owned YAML that would not parse, in the parser's own words (R10.2, R10.3).</summary>
    private static IEnumerable<DiagramProblem> UnreadableFiles(HelmChart chart)
    {
        if (chart.MetadataFailure is { } metadata)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, metadata);
        }

        if (chart.DependenciesFailure is { } dependencies)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, dependencies);
        }

        foreach (var values in chart.Values.Where(values => values.Failure is not null))
        {
            yield return Unreadable(DiagramProblemSeverity.Error, values.Failure!);
        }

        if (chart.Lock?.Failure is { } chartLock)
        {
            yield return Unreadable(DiagramProblemSeverity.Error, chartLock);
        }

        foreach (var crd in chart.Crds?.Failures ?? [])
        {
            yield return Unreadable(DiagramProblemSeverity.Error, crd);
        }

        static DiagramProblem Unreadable(DiagramProblemSeverity severity, TolerantYamlFailure failure) => new(
            severity,
            $"'{failure.RelativePath}' is not readable YAML: {failure.Message}",
            HelmRules.UnreadableYaml,
            new DiagramProblemFileLocation(failure.RelativePath, failure.Line));
    }

    /// <summary>The fields Helm itself requires, and the shape it requires of one of them (R10.2, R10.7).</summary>
    private static IEnumerable<DiagramProblem> Metadata(HelmChart chart)
    {
        if (chart.MetadataFailure is not null)
        {
            // Unreadable is already reported; guessing at what an unparseable file lacks
            // would stack noise on top of the real message.
            yield break;
        }

        var line = chart.Metadata?.Line ?? 0;
        if (chart.Metadata?.Name is not { Length: > 0 })
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                "Chart.yaml declares no name; Helm requires one.",
                HelmRules.MissingName,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }

        if (chart.Metadata?.Version is not { Length: > 0 })
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                "Chart.yaml declares no version; Helm requires one.",
                HelmRules.MissingVersion,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }
        else if (!_semVer.IsMatch(chart.Metadata.Version))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"'{chart.Metadata.Version}' is not a SemVer version; Helm expects MAJOR.MINOR.PATCH.",
                HelmRules.VersionNotSemVer,
                new DiagramProblemFileLocation("Chart.yaml", chart.Metadata.VersionLine));
        }

        if (chart.Legacy)
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "This is a legacy Helm 2 chart (apiVersion: v1); dependencies live in requirements.yaml.",
                HelmRules.LegacyChart,
                new DiagramProblemFileLocation("Chart.yaml", line));
        }
    }

    /// <summary>
    /// Undeclared vendored content (R10.4). The Unvendored state deliberately never fires:
    /// the prometheus example would open red otherwise, and unvendored is the common case.
    /// </summary>
    private static IEnumerable<DiagramProblem> Vendoring(HelmChart chart) =>
        DependencyResolution.Match(chart.Dependencies, chart.Vendored).Undeclared
            .Select(entry => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"'{entry.RelativePath}' sits in charts/ but no dependency declares it.",
                HelmRules.UndeclaredVendored,
                new DiagramProblemFileLocation(entry.RelativePath)));

    /// <summary>Lock drift, both directions - only when a lock exists at all (R10.5).</summary>
    private static IEnumerable<DiagramProblem> LockDrift(HelmChart chart)
    {
        if (chart.Lock is not { Failure: null } chartLock)
        {
            yield break;
        }

        var declared = chart.Dependencies.Select(dependency => dependency.Name)
            .ToHashSet(StringComparer.Ordinal);
        var locked = chartLock.Entries.Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in declared.Where(name => !locked.Contains(name)).Order(StringComparer.Ordinal))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The dependency '{name}' is declared but not in {chartLock.RelativePath}; run helm dependency update.",
                HelmRules.LockDrift,
                new DiagramProblemFileLocation(chartLock.RelativePath));
        }

        foreach (var entry in chartLock.Entries.Where(entry => !declared.Contains(entry.Name)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"{chartLock.RelativePath} pins '{entry.Name}', which no dependency declares; the lock is stale.",
                HelmRules.LockDrift,
                new DiagramProblemFileLocation(chartLock.RelativePath, entry.Line));
        }
    }

    /// <summary>An application chart that renders nothing (R10.6); library charts are exempt (R1.2).</summary>
    private static IEnumerable<DiagramProblem> Templates(HelmChart chart)
    {
        if (chart.Metadata is null || chart.Metadata.IsLibrary || chart.Templates.Count > 0)
        {
            yield break;
        }

        yield return new DiagramProblem(
            DiagramProblemSeverity.Warning,
            "This application chart has no templates; helm install would render nothing.",
            HelmRules.EmptyTemplates,
            new DiagramProblemFileLocation("Chart.yaml", chart.Metadata.Line));
    }

    /// <summary>A condition switching a subchart off a path the default values do not carry (R10.6).</summary>
    private static IEnumerable<DiagramProblem> Conditions(HelmChart chart)
    {
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        foreach (var dependency in chart.Dependencies.Where(dependency => dependency.State == ConditionState.Missing))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The condition '{dependency.Condition}' of dependency '{dependency.EffectiveName}' names a path that is not in values.yaml.",
                HelmRules.ConditionMissing,
                new DiagramProblemFileLocation(declaringFile, dependency.Line));
        }
    }

    /// <summary>Two dependencies mounting under one effective name cannot coexist (R10.6).</summary>
    private static IEnumerable<DiagramProblem> Collisions(HelmChart chart)
    {
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        foreach (var group in chart.Dependencies
                     .GroupBy(dependency => dependency.EffectiveName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var second = group.Skip(1).First();
            yield return new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"Two dependencies resolve to the effective name '{group.Key}'; give one an alias.",
                HelmRules.NameCollision,
                new DiagramProblemFileLocation(declaringFile, second.Line));
        }
    }
}

/// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
internal static class HelmRules
{
    /// <summary>The registered folder holds no <c>Chart.yaml</c> at all.</summary>
    public const string NotAChart = "helm.not-a-chart";

    /// <summary>A chart-owned YAML file does not parse, reported in the parser's own words.</summary>
    public const string UnreadableYaml = "helm.unreadable-yaml";

    /// <summary><c>Chart.yaml</c> lacks the <c>name</c> Helm requires.</summary>
    public const string MissingName = "helm.missing-name";

    /// <summary><c>Chart.yaml</c> lacks the <c>version</c> Helm requires.</summary>
    public const string MissingVersion = "helm.missing-version";

    /// <summary>The declared <c>version</c> is not SemVer-shaped.</summary>
    public const string VersionNotSemVer = "helm.version-not-semver";

    /// <summary>Something sits in <c>charts/</c> that no dependency declares.</summary>
    public const string UndeclaredVendored = "helm.undeclared-vendored";

    /// <summary>The lock and the declarations disagree - the lock is stale.</summary>
    public const string LockDrift = "helm.lock-drift";

    /// <summary>An application chart with no templates renders nothing.</summary>
    public const string EmptyTemplates = "helm.empty-templates";

    /// <summary>A dependency's <c>condition</c> path is not in the default values.</summary>
    public const string ConditionMissing = "helm.condition-missing";

    /// <summary>Two dependencies mount under the same effective name.</summary>
    public const string NameCollision = "helm.name-collision";

    /// <summary>An <c>apiVersion: v1</c> chart - legacy Helm 2 format.</summary>
    public const string LegacyChart = "helm.legacy-chart";
}
