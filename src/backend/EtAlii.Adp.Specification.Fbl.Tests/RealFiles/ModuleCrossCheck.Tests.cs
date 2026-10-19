using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.CausalLoopDiagram;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.HelmChart;
using EtAlii.Adp.Diagram.HelmChart.Tests.Parity;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// Requirement 11.6: for the four bindings whose module has a public reader, the ids per type the
/// binding reads from each real file equal the ids the module's own reader reads (for the timeline and
/// the causal loop diagram, the module's own binding), mapped from the
/// module's kinds to the binding's types by <see cref="ModuleIds"/>. Every difference is a listed
/// divergence. Only public API of the modules is used, and nothing in them is changed. The dependency
/// graph's check moved to the module's own test project with its parser (dependency-graph-disl-fbl, task 8).
/// <para>
/// <b>The Helm chart folder</b> (helm-chart-disl-fbl, Requirement 8.5): every folder of this repository that holds a
/// <c>Chart.yaml</c>, read through the vendored folder binding and the module's plugin by the library's folder body,
/// against today's reader, which the module's test project keeps as its oracle and this project compiles from
/// there, with the ids its graph gave each node and relation. The folder body is given nothing beside the binding, so a directory no file rule can select (gap 3 of
/// the definition's companion, which the module answers with entries of its own) shows here as a divergence.
/// </para>
/// <para>
/// <b>Databricks</b> (databricks-disl-fbl, task 1): every file the two vendored Databricks bindings take, against what
/// the module's three parsers read of it through <see cref="DatabricksModel.Read"/>. The job binding is opened once per
/// job the module reads, with that job's key as the <c>resource</c> header, and compared with that job; the resources
/// it captures are compared with the job resources the bundle parser reads.
/// </para>
/// </summary>
public class ModuleCrossCheckTests
{
    /// <summary>How many ids of a difference the observation names before it counts the rest.</summary>
    private const int Named = 5;

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in ModuleIds.Keys)
        {
            foreach (var file in RealFileCorpus.Files(RealFileCorpus.Find(key))) data.Add(key, file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheBindingReadsTheIdsTheModuleReads(string key, string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = RealFileCorpus.Binding(RealFileCorpus.Find(key));
        var text = Encoding.UTF8.GetString(bytes);

        // Act.
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.Model.Unreadable) return;
        var fbl = ModuleIds.OfBinding(key, body.Model);
        var module = ModuleIds.OfModule(key, text);

        // Assert.
        Divergences.Check("cross-check", key, file, Difference(fbl, module));
    }

    public static TheoryData<string> ChartFolders()
    {
        var data = new TheoryData<string>();
        foreach (var file in RealFileCorpus.All.Where(file => Path.GetFileName(file) == "Chart.yaml")) data.Add(file[..^"/Chart.yaml".Length]);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChartFolders))]
    public void TheHelmBindingAndItsPluginReadTheIdsTheModuleReads(string folder)
    {
        // Arrange.
        var full = RealFileCorpus.FullPath(folder);
        var binding = RealFileCorpus.Binding("helm-chart.fbl", "chart");

        // Act.
        var reading = FolderBody.ReadOnce(full, binding, new HelmChartFolderPlugin());
        Assert.False(reading.Unreadable, $"{folder} holds a Chart.yaml and is not read as a chart.");
        var fbl = ModuleIds.OfHelmBinding(reading);
        var module = ModuleIds.OfHelmModule(full);

        // Assert.
        Divergences.Check("cross-check", "helm-chart", folder, Difference(fbl, module));
    }

    public static TheoryData<string, string> DatabricksPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in ModuleIds.DatabricksKeys)
        {
            foreach (var file in RealFileCorpus.Files(RealFileCorpus.Find(key))) data.Add(key, file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(DatabricksPairs))]
    public void TheDatabricksBindingsReadTheIdsTheModuleReads(string key, string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = RealFileCorpus.Binding(RealFileCorpus.Find(key));
        var module = DatabricksModel.Outcome(LineDocument.Parse(Encoding.UTF8.GetString(bytes))).Model;

        // Act.
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.Model.Unreadable) return;
        var parts = new List<string>();
        if (key == "databricks-job")
        {
            Add(parts, "", Difference(ModuleIds.OfDatabricksResources(body.Model), ModuleIds.OfDatabricksBundle(module)));
            foreach (var job in module.Jobs)
            {
                var headers = new Dictionary<string, string> { ["resource"] = job.Key };
                var reading = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file, headers)).Model;
                Add(parts, $"job {job.Key}: ", Difference(ModuleIds.OfDatabricksBinding(reading), ModuleIds.OfDatabricksJob(job)));
            }
            if (module.Jobs.Count == 0) Add(parts, "no job: ", Difference(ModuleIds.OfDatabricksBinding(body.Model), new Dictionary<string, SortedSet<string>>()));
        }
        else
        {
            Add(parts, "", Difference(ModuleIds.OfDatabricksBinding(body.Model), ModuleIds.OfDatabricksPipelines(module)));
        }

        // Assert.
        Divergences.Check("cross-check", key, file, parts.Count == 0 ? null : string.Join(" | ", parts));

        static void Add(List<string> into, string prefix, string? difference)
        {
            if (difference is not null) into.Add(prefix + difference);
        }
    }

    private static string? Difference(IReadOnlyDictionary<string, SortedSet<string>> fbl, IReadOnlyDictionary<string, SortedSet<string>> module)
    {
        var parts = new List<string>();
        foreach (var type in fbl.Keys.Union(module.Keys).Order(StringComparer.Ordinal))
        {
            var read = fbl.GetValueOrDefault(type) ?? [];
            var expected = module.GetValueOrDefault(type) ?? [];
            var lacks = expected.Except(read).ToList();
            var adds = read.Except(expected).ToList();
            if (lacks.Count == 0 && adds.Count == 0) continue;
            var part = new StringBuilder(type).Append(':');
            if (lacks.Count > 0) part.Append(" the binding lacks ").Append(List(lacks));
            if (adds.Count > 0) part.Append(lacks.Count > 0 ? ";" : "").Append(" the binding adds ").Append(List(adds));
            parts.Add(part.ToString());
        }
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string List(List<string> ids) =>
        $"{ids.Count} ({string.Join(", ", ids.Take(Named))}{(ids.Count > Named ? ", …" : "")})";
}

/// <summary>
/// The ids per type each side reads, made comparable: the type-mapping table of Requirement 11.6,
/// and the transforms the modules' own id schemes need.
/// </summary>
internal static partial class ModuleIds
{
    public static readonly IReadOnlyList<string> Keys = ["timeline", "causal-loop", "structurizr", "mindmap"];

    /// <summary>The C4 kinds the binding has rules for; the others (components, deployment) it does not read.</summary>
    private static readonly IReadOnlyDictionary<C4ElementKind, string> _c4Types = new Dictionary<C4ElementKind, string>
    {
        [C4ElementKind.Person] = "Person",
        [C4ElementKind.SoftwareSystem] = "SoftwareSystem",
        [C4ElementKind.Container] = "Container",
    };

    /// <summary>The ids C4Parser makes for an element without an identifier: kind, name slug, counter.</summary>
    [GeneratedRegex("^(person|softwaresystem|container)_[A-Za-z0-9]+_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedC4Id();

    /// <summary>
    /// The binding's side: elements by type with their stored ids (an element without one is
    /// addressed by its place, which no module knows), and relations by their ends.
    /// </summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfBinding(string key, FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            string? id = (key, element.IsRelation) switch
            {
                ("structurizr", true) => $"{element.Source}->{element.Target}",
                ("mindmap", true) => $"branch:{element.Source}->{element.Target}",
                (_, true) => element.Id,
                _ when !element.IdIsStored && key is not "causal-loop" => null,
                _ => element.Id,
            };
            if (id is null || id.Contains('@', StringComparison.Ordinal)) continue;
            Add(ids, element.Type, id);
        }
        return ids;
    }

    /// <summary>The module's side, through its own public reader.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfModule(string key, string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        switch (key)
        {
            case "timeline":
                // The module has no parser of its own since timeline-disl-fbl task 5: its side is what its
                // body reads through the module's own binding, after its stand-in has asked whether the
                // body is YAML at all. A body YamlDotNet refuses, such as one with a key written twice, is
                // one the module reads nothing of: it reports timeline.unparseable. The vendored example
                // may still read it, which is then a listed divergence. An entry is named by the id it is
                // written with, the module binding's storedId, as the module finds it.
                if (TimelineYamlSyntax.Check(text) is not null) break;
                foreach (var element in TimelineBody.Parse(text).Model.Elements)
                {
                    if (element.Attributes.GetValueOrDefault("storedId") is string { Length: > 0 } stored) Add(ids, element.Type, stored);
                }
                break;
            case "causal-loop":
                // The module has no parser of its own since causal-loop-disl-fbl task 7: its side is what its
                // body reads, the module's own options over the module's embedded binding. The binding's ids
                // are DISL's (causal-loop-diagram.dis: natural, with its prefixes), which the module's model
                // gives its links and loops but not its variables.
                var loop = CausalLoopBody.Parse(text).Read();
                foreach (var variable in loop.Variables) Add(ids, "Variable", $"variable:{variable.Id}");
                foreach (var link in loop.Links) Add(ids, "Link", link.Id);
                foreach (var cycle in loop.Loops) Add(ids, "Loop", cycle.Id);
                break;
            case "structurizr":
                var workspace = C4Parser.Parse(LineDocument.Parse(text));
                foreach (var element in workspace.Elements)
                {
                    if (_c4Types.TryGetValue(element.Kind, out var type) && !GeneratedC4Id().IsMatch(element.Id)) Add(ids, type, element.Id);
                }
                foreach (var relationship in workspace.Relationships) Add(ids, "Relationship", $"{relationship.SourceId}->{relationship.DestinationId}");
                break;
            case "mindmap":
                var map = MindmapDocument.Parse(text, assignMissingIds: false);
                foreach (var node in map.Nodes.Where(n => n.Id.Length > 0))
                {
                    Add(ids, "Node", node.Id);
                    if (node.Parent is { Id.Length: > 0 } parent) Add(ids, "Branch", $"branch:{parent.Id}->{node.Id}");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "No module parser is cross-checked for this binding.");
        }
        return ids;
    }

    /// <summary>The Helm binding's side: every element the plugin delivered, by its type, with the id it delivered it with.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfHelmBinding(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements) Add(ids, element.Type, element.Id);
        return ids;
    }

    /// <summary>
    /// The Helm module's side: what today's reader, kept as the module's oracle (<see cref="HandWrittenHelmReader"/>),
    /// reads from the folder, each node by the definition's type for its kind with the id the module's graph gave it,
    /// and each <c>Includes</c> and <c>Resolves</c> by its id: <c>edge:</c>, its source, its type, its target (empty for
    /// an open end) and its label, <c>|</c> between each.
    /// </summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfHelmModule(string folder)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var chart = new HandWrittenHelmReader().Read(folder);
        if (!chart.IsChart) return ids;

        Add(ids, "Chart", "chart");
        foreach (var values in chart.Values) Add(ids, "Values", $"values:{values.RelativePath}");
        if (chart.Schema is { } schema) Add(ids, "Schema", $"schema:{schema.RelativePath}");
        var partials = chart.Templates.Where(template => template.Role == TemplateRole.Partial).ToList();
        foreach (var template in chart.Templates)
        {
            Add(ids, template.Role == TemplateRole.Partial ? "Partial" : "Template", $"tpl:{template.RelativePath}");
            foreach (var reference in template.Facts.References)
            {
                var defining = partials.FirstOrDefault(partial => partial.Facts.Defines.Contains(reference, StringComparer.Ordinal));
                Add(ids, "Includes", $"edge:tpl:{template.RelativePath}|Includes|{(defining is null ? "" : $"tpl:{defining.RelativePath}")}|{reference}");
            }
        }
        if (chart.Crds is not null) Add(ids, "Crds", "crds");
        foreach (var entry in chart.Vendored) Add(ids, entry.Sealed ? "Archive" : "Subchart", Vendored(entry));
        foreach (var resolved in DependencyResolution.Match(chart.Dependencies, chart.Vendored).Dependencies)
        {
            var dependency = $"dep:{resolved.Dependency.EffectiveName}";
            Add(ids, "Dependency", dependency);
            Add(ids, "Resolves", resolved.Vendored is { } vendored
                ? $"edge:{dependency}|Resolves|{Vendored(vendored)}|"
                : $"edge:{dependency}|Resolves||{resolved.Dependency.EffectiveName}");
        }
        if (chart.Lock is { } chartLock) Add(ids, "Lock", $"lock:{chartLock.RelativePath}");
        return ids;

        static string Vendored(VendoredEntry entry) => $"{(entry.Sealed ? "tgz" : "sub")}:{entry.RelativePath}";
    }

    /// <summary>The two Databricks bindings the cross-check reads, by their corpus keys.</summary>
    public static readonly IReadOnlyList<string> DatabricksKeys = ["databricks-job", "databricks-pipeline"];

    /// <summary>A Databricks binding's side: every element and relation, by its type, with the natural id it was read with.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfDatabricksBinding(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements) Add(ids, element.Type, element.Id);
        return ids;
    }

    /// <summary>The job binding's resources: the values of its <c>job</c> capture.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfDatabricksResources(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var resource in model.Resources) Add(ids, "Resource", resource);
        return ids;
    }

    /// <summary>The bundle parser's job resources, the module's side of <see cref="OfDatabricksResources"/>.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfDatabricksBundle(DatabricksModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var resource in model.Bundle.Resources.Where(resource => resource.Kind == "jobs")) Add(ids, "Resource", resource.Key);
        return ids;
    }

    /// <summary>
    /// One job as the job parser reads it, under the ids the module draws: <c>task:</c> and <c>cluster:</c> with the key,
    /// and <c>edge:</c> upstream <c>-&gt;</c> task for every dependency, a missing upstream included.
    /// </summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfDatabricksJob(JobModel job)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            Add(ids, "Task", $"task:{task.Key}");
            foreach (var dependency in task.DependsOn) Add(ids, "Dependency", $"edge:{dependency.TaskKey}->{task.Key}");
        }
        foreach (var cluster in job.Clusters) Add(ids, "JobCluster", $"cluster:{cluster.Key}");
        return ids;
    }

    /// <summary>The pipelines as the pipeline parser reads them: the fixed <c>pipeline</c>, and <c>library:</c> with each library's path.</summary>
    public static IReadOnlyDictionary<string, SortedSet<string>> OfDatabricksPipelines(DatabricksModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var pipeline in model.Pipelines)
        {
            Add(ids, "Pipeline", "pipeline");
            foreach (var library in pipeline.Libraries) Add(ids, "Library", $"library:{library.Path}");
        }
        return ids;
    }

    private static void Add(Dictionary<string, SortedSet<string>> ids, string type, string id)
    {
        if (!ids.TryGetValue(type, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            ids[type] = set;
        }
        set.Add(id);
    }
}
