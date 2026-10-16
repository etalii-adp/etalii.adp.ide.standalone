using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>helm-chart.fbl#chart</c>
/// (<c>net.etalii.adp.helm.chartFolder</c>): it reads a chart folder with <see cref="HelmChartReader"/>, the
/// module's own reading, from the files the folder body hands it, and delivers what the definition's model is
/// built from (helm-chart-disl-fbl, Requirements 3.2 to 3.6 and 6.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a plugin</b> (FBL 11.5, Q1): every scalar of the chart's own YAML is kept as written, the templates
/// are scanned line by line and never parsed, a condition is walked into the default values, and two of the
/// relations are matched across files. The whole folder stays one plugin read until FBL answers gaps 1, 2 and 5.
/// </para>
/// <para>
/// <b>It opens no file</b> (Requirement 3.2): it is handed the selected files, by relative path and bytes, and
/// the directories <see cref="HelmFolderEntries"/> lists, and keeps nothing it read. A file that does not parse,
/// one handed without bytes and one larger than the host reads each cost that file's detail and nothing else;
/// reading never throws on content.
/// </para>
/// <para>
/// <b>What it delivers</b>: one element per node, in today's order (the chart, the values layers, the schema,
/// the templates, the CRDs, what <c>charts/</c> holds, the dependencies, the lock), each with the attributes the
/// definition declares for it and the file and line it is read at; then <c>Includes</c> and <c>Resolves</c> from
/// <see cref="HelmOpenEndRelations"/>, a relation without a target for an open end. The definition computes every
/// id again and derives <c>Declares</c>, <c>Overrides</c> and <c>Configures</c>. Every chart-owned YAML file that
/// does not parse is one <c>std.unparseable</c> at its file and the parser's line, in the order the tool reports
/// them: <c>Chart.yaml</c>, <c>requirements.yaml</c>, the values files, the lock, the CRDs.
/// </para>
/// <para>
/// <b>Where an element is read</b> is what a finding located at it reports (DISL 8.2): the chart is at the start
/// of <c>Chart.yaml</c>'s root mapping, a dependency at its entry in the declaring file, and a file's node in that
/// file without a line. No finding is located at a folder's node, so the <c>crds/</c> folder and an unpacked
/// subchart are given no file.
/// </para>
/// <para>
/// <b>It reads; it does not plan.</b> The binding is read-only, so the host never asks it for a plan, and it
/// refuses one with the binding's sentence; a chart is never created here, so a template is never asked for.
/// </para>
/// </remarks>
public sealed class HelmChartFolderPlugin : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    private const string PluginId = "net.etalii.adp.helm.chartFolder";

    private readonly HelmChartReader _reader;

    /// <summary>The plugin over <paramref name="reader"/>.</summary>
    public HelmChartFolderPlugin(HelmChartReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>The plugin over the module's own reader.</summary>
    public HelmChartFolderPlugin()
        : this(new HelmChartReader())
    {
    }

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Result(_reader.Read(request));
    }

    /// <summary>What <see cref="Read"/> delivers for a chart already read: its elements and a finding per chart-owned YAML file that did not parse.</summary>
    internal static PluginReadResult Result(HelmChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new PluginReadResult(Elements(chart), Findings(chart), Unreadable: false);
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request) =>
        new PluginPlanResult.Refused(HelmDefinition.Binding.ReadOnly is { Length: > 0 } reason ? reason : "A helm chart diagram changes nothing in its folder.");

    /// <inheritdoc />
    public byte[] Template(PluginTemplateRequest request) =>
        throw new InvalidOperationException("A helm chart diagram is a folder that is already there, so its plugin is never asked for a template.");

    /// <inheritdoc />
    /// <remarks>Nothing beyond the file rules: a directory the reading counts is under a folder a file rule's glob could add a file to.</remarks>
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    /// <summary>The id of a template's node.</summary>
    internal static string TemplateId(TemplateFile template) => $"tpl:{template.RelativePath}";

    /// <summary>The id of a dependency's node: its effective name, so two dependencies of one effective name share it.</summary>
    internal static string DependencyId(DependencyDeclaration dependency) => $"dep:{dependency.EffectiveName}";

    /// <summary>The id of what <c>charts/</c> holds: an archive or an unpacked subchart.</summary>
    internal static string VendoredId(VendoredEntry entry) => $"{(entry.Sealed ? "tgz" : "sub")}:{entry.RelativePath}";

    /// <summary>The nodes in today's order, then the two relations that may have an open end.</summary>
    private static List<FblElement> Elements(HelmChart chart)
    {
        var resolution = DependencyResolution.Match(chart.Dependencies, chart.Vendored);
        var elements = new List<FblElement>
        {
            Node("chart", "Chart", Chart(chart), "Chart.yaml", chart.Metadata?.Line ?? 0),
        };

        foreach (var values in chart.Values)
        {
            elements.Add(Node($"values:{values.RelativePath}", "Values", Values(values), values.RelativePath, 0));
        }

        if (chart.Schema is { } schema)
        {
            elements.Add(Node($"schema:{schema.RelativePath}", "Schema", Path(schema.RelativePath), schema.RelativePath, 0));
        }

        foreach (var template in chart.Templates)
        {
            elements.Add(Node(TemplateId(template), template.Role == TemplateRole.Partial ? "Partial" : "Template", Template(template), template.RelativePath, 0));
        }

        if (chart.Crds is { } crds)
        {
            elements.Add(Node("crds", "Crds", Crds(crds), null, 0));
        }

        // What is actually vendored, whether or not anything declares it: the content is drawn.
        foreach (var entry in chart.Vendored)
        {
            elements.Add(entry.Sealed
                ? Node(VendoredId(entry), "Archive", Vendored(entry), entry.RelativePath, 0)
                : Node(VendoredId(entry), "Subchart", Vendored(entry), null, 0));
        }

        // A dependency is read at its entry in the file that declares it: requirements.yaml for a legacy chart.
        var declaringFile = chart.Legacy ? "requirements.yaml" : "Chart.yaml";
        foreach (var resolved in resolution.Dependencies)
        {
            elements.Add(Node(DependencyId(resolved.Dependency), "Dependency", Dependency(resolved.Dependency), declaringFile, resolved.Dependency.Line));
        }

        if (chart.Lock is { } chartLock)
        {
            elements.Add(Node($"lock:{chartLock.RelativePath}", "Lock", Lock(chartLock), chartLock.RelativePath, 0));
        }

        foreach (var relation in HelmOpenEndRelations.Of(chart, resolution))
        {
            elements.Add(new FblElement(
                relation.Id,
                IdIsStored: false,
                relation.Type,
                Rule: relation.Type,
                IsRelation: true,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = relation.Label },
                ParentId: null,
                ParentSlot: null,
                relation.SourceId,
                relation.TargetId,
                default,
                0));
        }

        return elements;
    }

    /// <summary>
    /// The chart-owned YAML that did not parse, one <c>std.unparseable</c> per file at the parser's line, in the
    /// order the tool reports them. Its message is the tool's whole sentence, which the built-in gives as it is.
    /// </summary>
    private static List<Finding> Findings(HelmChart chart)
    {
        TolerantYamlFailure?[] failures =
        [
            chart.MetadataFailure,
            chart.DependenciesFailure,
            .. chart.Values.Select(values => values.Failure),
            chart.Lock?.Failure,
            .. chart.Crds?.Failures ?? [],
        ];
        return [.. failures.OfType<TolerantYamlFailure>().Select(failure => new Finding(
            FindingCodes.Unparseable,
            FindingSeverity.Error,
            $"'{failure.RelativePath}' is not readable YAML: {failure.Message}",
            new SourceLocation(failure.RelativePath, (int)failure.Line, failure.Line == 0 ? 0 : 1, 0)))];
    }

    /// <summary>One node of the reading.</summary>
    private static FblElement Node(string id, string type, Dictionary<string, object?> attributes, string? file, uint line) =>
        new(id, IdIsStored: false, type, Rule: type, IsRelation: false, attributes, ParentId: null, ParentSlot: null, null, null, default, (int)line)
        {
            File = file,
        };

    private static Dictionary<string, object?> Chart(HelmChart chart)
    {
        var attributes = Path("Chart.yaml");
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

    private static Dictionary<string, object?> Values(ValuesFile values)
    {
        var attributes = Path(values.RelativePath);
        attributes["isDefault"] = values.IsDefault;
        attributes["keys"] = Strings(values.TopLevelKeys);
        Set(attributes, "failure", Failure(values.Failure));
        return attributes;
    }

    private static Dictionary<string, object?> Template(TemplateFile template)
    {
        var attributes = Path(template.RelativePath);
        attributes["kinds"] = Strings(template.Facts.Kinds);
        attributes["undetermined"] = template is { Role: TemplateRole.Manifest, Facts.Kinds.Count: 0 };
        attributes["defines"] = Strings(template.Facts.Defines);
        attributes["references"] = Strings(template.Facts.References);
        return attributes;
    }

    private static Dictionary<string, object?> Crds(CrdsSummary crds)
    {
        var attributes = Path("crds");
        attributes["files"] = (long)crds.FileCount;
        attributes["failures"] = crds.Failures.Select(failure => (object?)Failure(failure)).ToList();
        return attributes;
    }

    private static Dictionary<string, object?> Dependency(DependencyDeclaration dependency)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = dependency.Name };
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

    private static Dictionary<string, object?> Vendored(VendoredEntry entry)
    {
        var attributes = Path(entry.RelativePath);
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

    private static Dictionary<string, object?> Lock(LockFile chartLock)
    {
        var attributes = Path(chartLock.RelativePath);
        attributes["entries"] = chartLock.Entries
            .Select(entry => (object?)new CelMap { ["name"] = entry.Name, ["version"] = entry.Version ?? "", ["line"] = (long)entry.Line })
            .ToList();
        Set(attributes, "failure", Failure(chartLock.Failure));
        return attributes;
    }

    private static Dictionary<string, object?> Path(string relativePath) =>
        new(StringComparer.Ordinal) { ["path"] = relativePath };

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
