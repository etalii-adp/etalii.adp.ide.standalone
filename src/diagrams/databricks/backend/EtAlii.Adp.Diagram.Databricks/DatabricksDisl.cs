using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The DISL model of one reading of a parsed Databricks file, built from what <see cref="BundleParser"/>,
/// <see cref="JobParser"/> or <see cref="PipelineParser"/> read, with hand-made <see cref="FblElement"/>
/// records through <see cref="DislModelBuilder.From(FblModel, DislSpecification)"/>, under the definition of
/// that reading (databricks-disl-fbl design, <i>DatabricksDisl</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the adapter of stage one</b> (databricks-disl-fbl Q2): FBL cannot yet serve three readings of one
/// body (gaps 1 to 3), so the body is still read by the module's parsers and this class turns that reading into
/// the model the definitions derive from. Stage two, which reads the body through one binding, deletes it.
/// </para>
/// <para>
/// <b>Ids.</b> Every element is given today's wire id as its model id: an element is read with its natural id
/// and the definition's <c>persistence.ids.prefix</c> makes it <c>task:</c>, <c>cluster:</c>, <c>target:</c> or
/// <c>library:</c> and the key, and its derived ids make <c>bundle</c>, <c>resource:&lt;kind&gt;/&lt;key&gt;</c>,
/// <c>unknown:&lt;path&gt;</c>, <c>override:…</c>, <c>edge:&lt;from&gt;-&gt;&lt;to&gt;</c> and the fixed
/// <c>pipeline</c>, <c>target</c>, <c>compute</c> and <c>notifications</c>. So stored positions apply with no
/// registration rewritten (Requirement 5.4).
/// </para>
/// <para>
/// <b>A second task of one key</b> is read with an ephemeral id no key can equal, and keeps the key as written in
/// its <c>key</c> attribute and in the host attribute <see cref="WrittenKey"/>: its model id is not the first
/// holder's, so <c>std.duplicateId</c> stays off and the duplicate is the definition's own rule, by
/// <c>positionIn</c>. A <c>depends_on</c> naming the key finds the first holder.
/// </para>
/// <para>
/// <b>A dependency on a missing task</b> is a relation with no source, its written key kept in
/// <c>upstreamKey</c>; the drawn stub of the missing task stays <see cref="DatabricksElementMapper"/>'s (finding A9).
/// </para>
/// <para>
/// <b>The elements the file has no entry for</b> are emitted by <see cref="Fixed"/> alone, which is finding A10 made
/// visible.
/// </para>
/// </remarks>
internal static class DatabricksDisl
{
    /// <summary>The host attribute holding the key a task is written with, also for a second holder of the key.</summary>
    public const string WrittenKey = "writtenKey";

    /// <summary>What no task key can begin with: the start of a later holder's ephemeral id.</summary>
    private const char Ephemeral = '\u0001';

    private static readonly ConditionalWeakTable<JobModel, DislModel> _jobs = new();
    private static readonly ConditionalWeakTable<BundleModel, DislModel> _bundles = new();
    private static readonly ConditionalWeakTable<PipelineModel, DislModel> _pipelines = new();

    /// <summary>The model of no resource, per definition: what a reading that selects nothing shows.</summary>
    private static readonly ConditionalWeakTable<DislSpecification, DislModel> _empty = new();

    /// <summary>The job reading of <paramref name="job"/> under <paramref name="specification"/>, built once per parsed job; empty for none. Read it, never change it.</summary>
    public static DislModel Job(JobModel? job, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return job is null ? Empty(specification) : _jobs.GetValue(job, read => Build(JobElements(read), specification));
    }

    /// <summary>The bundle reading of <paramref name="bundle"/> under <paramref name="specification"/>, built once per parsed bundle. Read it, never change it.</summary>
    public static DislModel Bundle(BundleModel bundle, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(specification);
        return _bundles.GetValue(bundle, read => Build(BundleElements(read), specification));
    }

    /// <summary>The pipeline reading of <paramref name="pipeline"/> under <paramref name="specification"/>, built once per parsed pipeline; empty for none. Read it, never change it.</summary>
    public static DislModel Pipeline(PipelineModel? pipeline, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return pipeline is null ? Empty(specification) : _pipelines.GetValue(pipeline, read => Build(PipelineElements(read), specification));
    }

    /// <summary>
    /// The element <paramref name="wireId"/> names in <paramref name="diagram"/>, as the module's selection finds
    /// it: the first node with that model id, else the first relation; null for none. A later holder of a task key
    /// has an ephemeral id, so it is never the one a wire id finds.
    /// </summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? wireId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        if (wireId is not { Length: > 0 }) return null;
        return diagram.Nodes.FirstOrDefault(node => string.Equals(node.Id, wireId, StringComparison.Ordinal))
            ?? diagram.Relations.FirstOrDefault(relation => string.Equals(relation.Id, wireId, StringComparison.Ordinal));
    }

    private static DislModel Empty(DislSpecification specification) =>
        _empty.GetValue(specification, empty => Build([], empty));

    private static DislModel Build(IReadOnlyList<FblElement> elements, DislSpecification specification) =>
        DislModelBuilder.From(new FblModel(elements, [], false), specification);

    /// <summary>
    /// The job's own scalars as the diagram's attributes, a task per task with a key, a job cluster per cluster, and
    /// a dependency per <c>depends_on</c> entry, from the task it names to the task it is written under.
    /// </summary>
    private static List<FblElement> JobElements(JobModel job)
    {
        var elements = new List<FblElement>
        {
            Diagram(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["jobKey"] = job.Key,
                ["name"] = Present(job.Name),
                ["schedule"] = Present(job.Schedule),
                ["continuous"] = job.Continuous,
            }),
        };

        var keyed = job.Tasks.Where(task => task.Key.Length > 0).ToList();
        var holders = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var task in keyed)
        {
            // The first holder of a key is read with it; a later one with an id no key can equal.
            var id = holders.Add(task.Key) ? task.Key : $"{Ephemeral}{ids.Count}{Ephemeral}{task.Key}";
            ids.Add(id);
            elements.Add(Node(id, "Task", Line(task.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = task.Key,
                ["taskType"] = task.Type,
                ["taskSource"] = Present(task.Source),
                ["clusterKey"] = Present(task.ClusterKey),
                ["runIf"] = Present(task.RunIf),
            }, new Dictionary<string, object?>(StringComparer.Ordinal) { [WrittenKey] = task.Key }));
        }

        foreach (var cluster in job.Clusters)
        {
            elements.Add(Node(cluster.Key, "JobCluster", Line(cluster.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = cluster.Key,
                ["sparkVersion"] = Present(cluster.SparkVersion),
                ["nodeType"] = Present(cluster.NodeType),
                ["workers"] = (long)cluster.Workers,
            }));
        }

        for (var index = 0; index < keyed.Count; index++)
        {
            foreach (var dependency in keyed[index].DependsOn)
            {
                // The upstream task is the first holder of the key it names, or none.
                var upstream = holders.Contains(dependency.TaskKey) ? dependency.TaskKey : null;
                elements.Add(Relation($"edge:{dependency.TaskKey}->{keyed[index].Key}", "Dependency", upstream, ids[index], Line(dependency.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["upstreamKey"] = dependency.TaskKey,
                    ["outcome"] = Present(dependency.Outcome),
                }));
            }
        }

        return elements;
    }

    /// <summary>
    /// The bundle, a resource per resource, an unknown node per piece of unmodelled configuration, a target per
    /// target and an override per entry of a target's own <c>resources:</c>, with the includes and variables as
    /// the diagram's attributes.
    /// </summary>
    private static List<FblElement> BundleElements(BundleModel bundle)
    {
        var elements = new List<FblElement>
        {
            Diagram(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["includes"] = bundle.Includes.Select(include => (object?)include.Glob).ToList(),
                ["variables"] = bundle.Variables.Select(variable => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = variable.Name,
                    ["default"] = variable.Default,
                    ["description"] = variable.Description,
                }).ToList(),
            }),
        };
        elements.AddRange(Fixed(bundle));
        elements.AddRange(bundle.Resources.Select(resource => Node($"resource:{resource.Kind}/{resource.Key}", "Resource", Line(resource.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resourceKind"] = resource.Kind,
            ["key"] = resource.Key,
        })));
        elements.AddRange(bundle.UnknownNodes.Select(unknown => Node($"unknown:{unknown.Path}", "Unknown", Line(unknown.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = unknown.Path,
            ["key"] = unknown.Key,
        })));
        foreach (var target in bundle.Targets)
        {
            elements.Add(Node(target.Name, "Target", Line(target.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = target.Name,
                ["mode"] = Present(target.Mode),
                ["isDefault"] = target.IsDefault,
            }));
        }

        foreach (var target in bundle.Targets)
        {
            foreach (var overridden in target.Overrides)
            {
                var resource = $"resource:{overridden.Kind}/{overridden.Key}";
                elements.Add(Relation(
                    $"override:{target.Name}/{overridden.Kind}/{overridden.Key}",
                    "Override",
                    target.Name,
                    bundle.Resources.Any(declared => $"resource:{declared.Kind}/{declared.Key}" == resource) ? resource : null,
                    Line(overridden.Lines),
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["resourceKind"] = overridden.Kind, ["resourceKey"] = overridden.Key }));
            }
        }

        return elements;
    }

    /// <summary>A library per entry of the pipeline's <c>libraries</c>, and the pipeline's own elements.</summary>
    private static List<FblElement> PipelineElements(PipelineModel pipeline)
    {
        var elements = new List<FblElement>
        {
            Diagram(new Dictionary<string, object?>(StringComparer.Ordinal) { ["pipelineKey"] = pipeline.Key }),
        };
        elements.AddRange(pipeline.Libraries.Select(library => Node(library.Path, "Library", Line(library.Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["libraryKind"] = library.Kind,
            ["path"] = library.Path,
        })));
        elements.AddRange(Fixed(pipeline));
        return elements;
    }

    /// <summary>
    /// The bundle reading's element the file need not have an entry for: the one bundle, drawn whether or not the
    /// file has a <c>bundle:</c> key, is empty or does not parse (finding A10).
    /// </summary>
    private static IEnumerable<FblElement> Fixed(BundleModel bundle)
    {
        yield return Node("bundle", "Bundle", 0, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = Present(bundle.Name) });
    }

    /// <summary>
    /// The pipeline reading's elements the file has no entry for (finding A10): the pipeline itself, its target and
    /// its compute, always, and its notifications when it has any.
    /// </summary>
    private static IEnumerable<FblElement> Fixed(PipelineModel pipeline)
    {
        var line = Line(pipeline.Lines);
        yield return Node("pipeline", "Pipeline", line, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = Present(pipeline.Name),
            ["catalog"] = Present(pipeline.Catalog),
            ["schema"] = Present(pipeline.Schema),
            ["serverless"] = pipeline.Serverless,
            ["continuous"] = pipeline.Continuous,
            ["development"] = pipeline.Development,
            ["channel"] = Present(pipeline.Channel),
        });
        yield return Node("target", "Destination", line, new Dictionary<string, object?>(StringComparer.Ordinal));
        yield return Node("compute", "Compute", line, new Dictionary<string, object?>(StringComparer.Ordinal));
        if (pipeline.Notifications.Count > 0)
        {
            yield return Node("notifications", "Notifications", Line(pipeline.Notifications[0].Lines), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entries"] = pipeline.Notifications.Select(notification => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["recipients"] = notification.Recipients.ToList<object?>(),
                    ["alerts"] = notification.Alerts.ToList<object?>(),
                }).ToList(),
            });
        }
    }

    /// <summary>A value the parser read as empty text is not written: absent, as the file does not state it.</summary>
    private static string? Present(string value) => value.Length > 0 ? value : null;

    /// <summary>The line an element is written at, 1-based, as a finding reports it.</summary>
    private static int Line(EtAlii.Adp.Documents.LineRange lines) => lines.Start + 1;

    private static FblElement Diagram(Dictionary<string, object?> attributes) =>
        new("diagram", false, "diagram", "diagram", false, Written(attributes), null, null, null, null, default, 0);

    private static FblElement Node(string id, string type, int line, Dictionary<string, object?> attributes, Dictionary<string, object?>? host = null)
    {
        var written = Written(attributes);
        if (host is not null)
        {
            foreach ((string name, object? value) in host)
            {
                written[name] = value;
            }
        }

        return new FblElement(id, true, type, type, false, written, null, null, null, null, default, line);
    }

    private static FblElement Relation(string id, string type, string? source, string? target, int line, Dictionary<string, object?> attributes) =>
        new(id, true, type, type, true, Written(attributes), null, null, source, target, default, line);

    /// <summary>The attributes the file states: an absent one is left out.</summary>
    private static Dictionary<string, object?> Written(Dictionary<string, object?> attributes) =>
        attributes.Where(attribute => attribute.Value is not null).ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
}
