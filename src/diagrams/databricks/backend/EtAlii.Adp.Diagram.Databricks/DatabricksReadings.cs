using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The three readings of one Databricks file as the module holds them (<see cref="DatabricksModel"/>), taken from the
/// binding's readings of its body (<see cref="DatabricksBody"/>), and the DISL model of each, built by
/// <see cref="DislModelBuilder"/> from that reading under its definition (databricks-disl-fbl Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>What stays module code, and why</b>: each is named for the gap it stands in for, in the companion pages too.
/// </para>
/// <list type="bullet">
/// <item><b>The elements the file has no entry for</b> (<see cref="FixedBundle"/>, <see cref="FixedPipeline"/>): the one
/// bundle, drawn whether or not the file has a <c>bundle:</c> key, and a pipeline's diagram key, destination, compute and
/// notifications - DISL gap 8, finding A10.</item>
/// <item><b>A dependency's downstream end</b> is the task its entry is written under, which the binding reads as the
/// element's parent; a dependency on a task the job does not declare keeps an unset upstream end and its written key,
/// as DISL 11.2 asks, and its stub stays <see cref="DatabricksElementMapper"/>'s - DISL gap 8, finding A9.</item>
/// <item><b>A value written empty is not stated</b> (<see cref="Unstated"/>): the module has always treated an empty
/// name, schedule, source, cluster, run_if, mode, catalog, schema or channel as absent.</item>
/// </list>
/// <para>
/// <b>Ids.</b> The binding's readings are given the definitions' natural ids (<see cref="DatabricksDefinition.ReadIds"/>),
/// so a task reads with its key and a target with its name, and the definitions' prefixes and derived ids make today's
/// wire ids: stored positions apply with no registration rewritten (Requirement 5.4). A second task of one key is read
/// at its place, so it keeps an id of its own.
/// </para>
/// </remarks>
internal static class DatabricksReadings
{
    private static readonly ConditionalWeakTable<FblModel, DislModel> _models = new();

    /// <summary>The model of no resource, per definition: what a reading that selects nothing shows.</summary>
    private static readonly ConditionalWeakTable<DislSpecification, DislModel> _empty = new();

    /// <summary>The attributes, per binding type, that the module treats as absent when they are written empty.</summary>
    private static readonly Dictionary<string, string[]> Unstated = new(StringComparer.Ordinal)
    {
        ["Job"] = ["name", "schedule"],
        ["Task"] = ["taskSource", "clusterKey", "runIf"],
        ["JobCluster"] = ["sparkVersion", "nodeType"],
        ["Bundle"] = ["name"],
        ["Target"] = ["mode"],
        ["Pipeline"] = ["name", "catalog", "schema", "channel"],
    };

    /// <summary>The three readings of <paramref name="body"/>.</summary>
    public static DatabricksModel Model(DatabricksBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new DatabricksModel(Bundle(body), Jobs(body), Pipelines(body));
    }

    /// <summary>The job reading of <paramref name="job"/> under <paramref name="specification"/>, built once per read job; empty for none. Read it, never change it.</summary>
    public static DislModel Job(JobModel? job, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return job?.Reading is { } reading ? _models.GetValue(reading, read => Build(JobElements(read), specification)) : Empty(specification);
    }

    /// <summary>The bundle reading of <paramref name="bundle"/> under <paramref name="specification"/>, built once per read bundle. Read it, never change it.</summary>
    public static DislModel Bundle(BundleModel bundle, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(specification);
        return bundle.Reading is { } reading
            ? _models.GetValue(reading, read => Build(BundleElements(read), specification))
            : _empty.GetValue(specification, empty => Build([.. FixedBundle(null)], empty));
    }

    /// <summary>The pipeline reading of <paramref name="pipeline"/> under <paramref name="specification"/>, built once per read pipeline; empty for none. Read it, never change it.</summary>
    public static DislModel Pipeline(PipelineModel? pipeline, DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return pipeline?.Reading is { } reading ? _models.GetValue(reading, read => Build(PipelineElements(pipeline, read), specification)) : Empty(specification);
    }

    /// <summary>The job reading of <paramref name="job"/> under <paramref name="specification"/>, located in the subject <paramref name="file"/>: built afresh.</summary>
    public static DislModel Job(JobModel job, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        return Build(job.Reading is { } reading ? JobElements(reading) : [], specification, file);
    }

    /// <summary>The bundle reading of <paramref name="bundle"/> under <paramref name="specification"/>, located in the subject <paramref name="file"/>: built afresh.</summary>
    public static DislModel Bundle(BundleModel bundle, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        return Build(bundle.Reading is { } reading ? BundleElements(reading) : [.. FixedBundle(null)], specification, file);
    }

    /// <summary>The pipeline reading of <paramref name="pipeline"/> under <paramref name="specification"/>, located in the subject <paramref name="file"/>: built afresh.</summary>
    public static DislModel Pipeline(PipelineModel pipeline, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        return Build(pipeline.Reading is { } reading ? PipelineElements(pipeline, reading) : [], specification, file);
    }

    /// <summary>
    /// The element <paramref name="wireId"/> names in <paramref name="diagram"/>, as the module's selection finds it: the
    /// first node with that model id, else the first relation; null for none.
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

    private static DislModel Build(IReadOnlyList<FblElement> elements, DislSpecification specification, string? file = null) =>
        DislModelBuilder.From(new FblModel(elements, [], false), specification, new DislEnv(), file);

    // ------------------------------------------------------------------ the readings as the module holds them

    private static BundleModel Bundle(DatabricksBody body)
    {
        var reading = body.Reading(DatabricksBody.BundleOrigin);
        var elements = reading.Elements;
        var overrides = elements.Where(element => element.Type == "Override").ToList();
        return new BundleModel(
            Text(elements.FirstOrDefault(element => element.Type == "Bundle"), "name"),
            [.. elements.Where(element => element.Type == "Resource").Select(element => new BundleResource(Text(element, "resourceKind"), Text(element, "key"), body.LinesOf(element.OwnSpan)))],
            [.. elements.Where(element => element.Type == "Target").Select(element => new BundleTarget(
                Text(element, "name"),
                Text(element, "mode"),
                element.Attributes.GetValueOrDefault("isDefault") is true,
                [.. overrides.Where(overridden => Text(overridden, "from") == Text(element, "name")).Select(overridden => new BundleResource(Text(overridden, "resourceKind"), Text(overridden, "resourceKey"), body.LinesOf(overridden.OwnSpan)))],
                body.LinesOf(element.OwnSpan)))],
            [.. elements.Where(element => element.Rule == "unknownKey").Concat(elements.Where(element => element.Rule == "unknownKind")).Select(element => new UnknownNode(Text(element, "path"), Text(element, "key"), body.LinesOf(element.OwnSpan)))])
        {
            Reading = reading,
        };
    }

    private static List<JobModel> Jobs(DatabricksBody body)
    {
        var jobs = new List<JobModel>();
        foreach (var key in body.Resources(DatabricksBody.JobOrigin))
        {
            var reading = body.Reading(DatabricksBody.JobOrigin, key);
            if (reading.Elements.FirstOrDefault(element => element.Type == "Job") is not { } job) continue;

            var dependencies = reading.Elements.Where(element => element.Type == "Dependency").ToList();
            jobs.Add(new JobModel(
                Text(job, "jobKey"),
                Text(job, "name"),
                [.. reading.Elements.Where(element => element.Type is "Task" or "KeylessTask").Select(task => new JobTask(
                    task.Type == "Task" ? Text(task, "key") : "",
                    Text(task, "taskType"),
                    Text(task, "taskSource"),
                    Text(task, "clusterKey"),
                    Text(task, "runIf"),
                    [.. dependencies.Where(dependency => dependency.ParentId == task.Id).Select(dependency => new JobDependency(Text(dependency, "upstreamKey"), Text(dependency, "outcome"), body.LinesOf(dependency.OwnSpan)))],
                    body.LinesOf(task.OwnSpan)))],
                [.. reading.Elements.Where(element => element.Type == "JobCluster").Select(cluster => new JobCluster(
                    Text(cluster, "key"),
                    Text(cluster, "sparkVersion"),
                    Text(cluster, "nodeType"),
                    cluster.Attributes.GetValueOrDefault("workers") is long workers ? (int)workers : 0,
                    body.LinesOf(cluster.OwnSpan)))],
                Text(job, "schedule"),
                job.Attributes.GetValueOrDefault("continuous") is true,
                body.LinesOf(job.OwnSpan))
            {
                Reading = reading,
            });
        }

        return jobs;
    }

    private static List<PipelineModel> Pipelines(DatabricksBody body)
    {
        var pipelines = new List<PipelineModel>();
        var keys = body.Resources(DatabricksBody.PipelineOrigin);
        foreach (var key in keys.Count > 0 ? keys.Cast<string?>() : [null])
        {
            var reading = body.Reading(DatabricksBody.PipelineOrigin, key);
            if (reading.Elements.FirstOrDefault(element => element.Type == "Pipeline") is not { } pipeline) continue;

            pipelines.Add(new PipelineModel(
                key ?? "",
                Text(pipeline, "name"),
                Text(pipeline, "catalog"),
                Text(pipeline, "schema"),
                pipeline.Attributes.GetValueOrDefault("serverless") is true,
                pipeline.Attributes.GetValueOrDefault("continuous") is true,
                pipeline.Attributes.GetValueOrDefault("development") is true,
                Text(pipeline, "channel"),
                [.. reading.Elements.Where(element => element.Type == "Library").Select(library => new PipelineLibrary(Text(library, "libraryKind"), Text(library, "path"), body.LinesOf(library.OwnSpan)))],
                [.. reading.Elements.Where(element => element.Type == "Notification").Select(notification => new PipelineNotification(Strings(notification, "recipients"), Strings(notification, "alerts"), body.LinesOf(notification.OwnSpan)))],
                body.LinesOf(pipeline.OwnSpan))
            {
                Reading = reading,
            });
        }

        return pipelines;
    }

    // ------------------------------------------------------------------ the readings as the definitions read them

    /// <summary>The job, its tasks with a key, its clusters, and a dependency per <c>depends_on</c> entry of a task with a key, from the task it names to the task it is written under.</summary>
    private static List<FblElement> JobElements(FblModel reading)
    {
        var keyed = reading.Elements.Where(element => element.Type == "Task").Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var elements = new List<FblElement>();
        elements.AddRange(reading.Elements.Where(element => element.Type == "Job").Select(Stated));
        elements.AddRange(reading.Elements.Where(element => element.Type == "Task").Select(Stated));
        elements.AddRange(reading.Elements.Where(element => element.Type == "JobCluster").Select(Stated));
        elements.AddRange(reading.Elements
            .Where(element => element is { Type: "Dependency", ParentId: { } parent } && keyed.Contains(parent))
            .Select(Downstream));
        return elements;
    }

    /// <summary>The bundle's attributes, the one bundle, a resource per resource, an unknown node per piece of unmodelled configuration, a target per target and an override per entry of a target's own <c>resources:</c>.</summary>
    private static List<FblElement> BundleElements(FblModel reading)
    {
        var elements = new List<FblElement>();
        elements.AddRange(reading.Elements.Where(element => element.Type == "BundleFile"));
        elements.AddRange(FixedBundle(reading.Elements.FirstOrDefault(element => element.Type == "Bundle")));
        elements.AddRange(reading.Elements.Where(element => element.Type == "Resource"));
        elements.AddRange(reading.Elements.Where(element => element.Rule == "unknownKey"));
        elements.AddRange(reading.Elements.Where(element => element.Rule == "unknownKind"));
        elements.AddRange(reading.Elements.Where(element => element.Type == "Target").Select(Stated));
        var declared = reading.Elements.Where(element => element.Type == "Resource").Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        elements.AddRange(reading.Elements.Where(element => element.Type == "Override").Select(overridden => Declared(overridden, declared)));
        return elements;
    }

    /// <summary>
    /// An override with its target end only when the bundle declares the resource it names (DISL gap 8, finding A9): the
    /// definition's <c>overrideOfUndeclared</c> reports one that names nothing, so it is not a reference to report too.
    /// </summary>
    private static FblElement Declared(FblElement overridden, HashSet<string> declared) =>
        Text(overridden, "to") is var target && declared.Contains(target)
            ? overridden
            : overridden with { Attributes = overridden.Attributes.Where(attribute => attribute.Key != "to").ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal) };

    /// <summary>A library per entry of the pipeline's <c>libraries</c>, and the pipeline's own elements.</summary>
    private static List<FblElement> PipelineElements(PipelineModel pipeline, FblModel reading)
    {
        var elements = new List<FblElement>();
        elements.AddRange(reading.Elements.Where(element => element.Type == "Library"));
        elements.AddRange(FixedPipeline(pipeline, reading));
        return elements;
    }

    /// <summary>
    /// The bundle reading's element the file need not have an entry for (DISL gap 8, finding A10): the one bundle, drawn
    /// whether or not the file has a <c>bundle:</c> key, is empty or does not parse, at no line of its own.
    /// </summary>
    private static IEnumerable<FblElement> FixedBundle(FblElement? bundle)
    {
        yield return bundle is not null
            ? Stated(bundle) with { Line = 0 }
            : new FblElement("bundle", false, "Bundle", "bundle", false, new Dictionary<string, object?>(StringComparer.Ordinal), null, null, null, null, default, 0);
    }

    /// <summary>
    /// The pipeline reading's elements the file has no entry for (DISL gap 8, finding A10): the diagram's key, the
    /// pipeline itself, its destination and its compute, always, and its notifications when it has any.
    /// </summary>
    private static IEnumerable<FblElement> FixedPipeline(PipelineModel pipeline, FblModel reading)
    {
        yield return new FblElement("diagram", false, "diagram", "diagram", false, new Dictionary<string, object?>(StringComparer.Ordinal) { ["pipelineKey"] = pipeline.Key }, null, null, null, null, default, 0);
        if (reading.Elements.FirstOrDefault(element => element.Type == "Pipeline") is not { } read) yield break;

        yield return Stated(read);
        yield return new FblElement("target", false, "Destination", "pipeline", false, new Dictionary<string, object?>(StringComparer.Ordinal), null, null, null, null, default, read.Line);
        yield return new FblElement("compute", false, "Compute", "pipeline", false, new Dictionary<string, object?>(StringComparer.Ordinal), null, null, null, null, default, read.Line);
        var notifications = reading.Elements.Where(element => element.Type == "Notification").ToList();
        if (notifications.Count > 0)
        {
            yield return new FblElement("notifications", false, "Notifications", "notification", false, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entries"] = notifications.Select(notification => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["recipients"] = Strings(notification, "recipients").ToList<object?>(),
                    ["alerts"] = Strings(notification, "alerts").ToList<object?>(),
                }).ToList(),
            }, null, null, null, null, default, notifications[0].Line);
        }
    }

    /// <summary>A dependency with its downstream end, the task its entry is written under (DISL gap 8, finding A9).</summary>
    private static FblElement Downstream(FblElement dependency) =>
        dependency with { Attributes = new Dictionary<string, object?>(dependency.Attributes, StringComparer.Ordinal) { ["to"] = dependency.ParentId }, ParentId = null };

    /// <summary>The element without the attributes it is written empty for, which the module has always read as absent.</summary>
    private static FblElement Stated(FblElement element) =>
        Unstated.TryGetValue(element.Type, out var names)
            ? element with { Attributes = element.Attributes.Where(attribute => !(names.Contains(attribute.Key) && attribute.Value is "")).ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal) }
            : element;

    /// <summary>An attribute as the module holds it: its text, empty when the file does not state it.</summary>
    private static string Text(FblElement? element, string attribute) =>
        element?.Attributes.GetValueOrDefault(attribute) switch
        {
            null => "",
            string text => text,
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        };

    private static IReadOnlyList<string> Strings(FblElement element, string attribute) =>
        element.Attributes.GetValueOrDefault(attribute) is IEnumerable<object?> values ? [.. values.OfType<string>()] : [];
}
