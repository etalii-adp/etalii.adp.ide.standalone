using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The DISL model of a pipeline as <see cref="PipelineParser"/> read it: the adapter that lets the
/// toolbox, the menus and the rows be derived from the bundled definition while the body is still read
/// and written by the module's own code (azure-devops-pipeline-disl-fbl task 6). At task 8 this
/// projection moves into the persistence plugin, which emits the same binding types.
/// </summary>
/// <remarks>
/// <para>
/// <b>The projection emits the binding types the definition's <c>typeMap</c> names</b> and leaves the
/// model to <see cref="DislModelBuilder"/>, so the derived ids (a stage's name, <c>stage/job</c>,
/// <c>job/step-n</c>) and the default order's derived relations are the definition's, not this
/// adapter's. Each element is read under an id of its own place in the reading, because the parser's
/// ids are not unique where two stages share a name (finding A8): the first is the one a gesture finds,
/// and a job is still beneath its own stage.
/// </para>
/// <para>
/// <b>A value is written only where the file writes it</b>, so <c>has()</c> tells an absent key from an
/// empty one, as the rows do (Requirement 13.10 of azure-pipeline-diagram).
/// </para>
/// <para>
/// <b>The model is built once per reading</b> and kept beside it; the viewer state it carries
/// (<c>self.view.collapsed</c>) is bound per call under a lock, because one reading is shared by every
/// connection that has the pipeline open.
/// </para>
/// </remarks>
internal static class PipelineDisl
{
    private static readonly ConditionalWeakTable<PipelineModel, DislDiagram> _built = [];

    /// <summary>The DISL model of <paramref name="model"/>, built the first time it is asked for.</summary>
    public static DislDiagram ModelOf(PipelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _built.GetValue(model, Build);
    }

    /// <summary>
    /// Binds every stage's and job's <c>self.view.collapsed</c> to <paramref name="view"/>, closed where
    /// it has nothing open (the definition's <c>view.initial</c>), and runs <paramref name="read"/> while
    /// the binding holds.
    /// </summary>
    public static T Viewed<T>(DislDiagram diagram, PipelineConnectionView? view, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(read);
        lock (diagram)
        {
            foreach (var node in diagram.Nodes.Where(node => node.IsA("Stage") || node.IsA("JobBase")))
            {
                node.View = new CelMap { ["collapsed"] = view is null || !view.IsExpanded(node.Id) };
            }

            return read();
        }
    }

    private static DislDiagram Build(PipelineModel model) =>
        DislModelBuilder.From(new FblModel(Elements(model), [], false), PipelineDefinition.Specification).Diagram;

    /// <summary>
    /// The reading as the binding's types: the pipeline, each stage with its jobs and their steps in
    /// written order, the template references not followed, and one <c>DependsOn</c> per name a stage or
    /// a job writes - its target the element that waits, its source the first of that name, without
    /// regard to case, among the stages or among the jobs of the same stage, or unset.
    /// </summary>
    private static IReadOnlyList<FblElement> Elements(PipelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var nodes = new List<FblElement> { Node("pipeline", "Pipeline", PipelineAttributes(model), null) };
        var relations = new List<FblElement>();
        var firstReadId = new Dictionary<string, string>(StringComparer.Ordinal);
        var next = 0;

        var stages = new List<(PipelineStage Stage, string Read, List<(PipelineJob Job, string Read)> Jobs)>();
        foreach (var stage in model.Stages)
        {
            var stageRead = Place(stage.IsImplicit ? "ImplicitStage" : "Stage", StageAttributes(stage), null, stage.Id);
            var jobs = new List<(PipelineJob Job, string Read)>();
            foreach (var job in stage.Jobs)
            {
                var jobRead = Place(job.IsImplicit ? "ImplicitJob" : job.IsDeployment ? "DeploymentJob" : "Job", JobAttributes(job), stageRead, job.Id);
                jobs.Add((job, jobRead));
                foreach (var step in job.Steps)
                {
                    Place("Step", StepAttributes(step), jobRead, step.Id);
                }
            }

            stages.Add((stage, stageRead, jobs));
        }

        foreach (var unresolved in model.Unresolved)
        {
            nodes.Add(Node(NextId(), "TemplateReference", TemplateAttributes(unresolved, firstReadId), null));
        }

        var stageNames = stages.Select(stage => (stage.Stage.Name, stage.Read)).ToList();
        foreach ((PipelineStage stage, string stageRead, List<(PipelineJob Job, string Read)> jobs) in stages)
        {
            Depend(stage.DependsOn, stageRead, stageNames);
            var jobNames = jobs.Select(job => (job.Job.Name, job.Read)).ToList();
            foreach ((PipelineJob job, string jobRead) in jobs)
            {
                Depend(job.DependsOn, jobRead, jobNames);
            }
        }

        return [.. nodes, .. relations];

        string Place(string type, IReadOnlyDictionary<string, object?> attributes, string? parent, string parsedId)
        {
            var id = NextId();
            nodes.Add(new FblElement(id, false, type, type, false, attributes, parent, parent is null ? null : "children", null, null, default, 0));
            firstReadId.TryAdd(parsedId, id);
            return id;
        }

        void Depend(IReadOnlyList<string> dependsOn, string waiting, List<(string Name, string Read)> candidates)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string read) in candidates.Where(candidate => candidate.Name.Length > 0))
            {
                byName.TryAdd(name, read);
            }

            foreach (var written in dependsOn)
            {
                relations.Add(new FblElement(
                    NextId(), false, "DependsOn", "DependsOn", true, Attributes(("name", written)),
                    null, null, byName.GetValueOrDefault(written), waiting, default, 0));
            }
        }

        string NextId() => $"read-{next++}";

        static FblElement Node(string id, string type, IReadOnlyDictionary<string, object?> attributes, string? parent) =>
            new(id, false, type, type, false, attributes, parent, null, null, null, default, 0);
    }

    /// <summary>The pipeline's pool, which every stage and job inherits unless it sets one, and what it extends.</summary>
    private static Dictionary<string, object?> PipelineAttributes(PipelineModel model)
    {
        var attributes = Attributes();
        var pipelinePool = model.Stages.Select(stage => stage.Pool)
            .Concat(model.Jobs.Select(job => job.Pool))
            .FirstOrDefault(pool => pool.Origin == PipelinePoolOrigin.Pipeline);
        if (pipelinePool is not null) attributes["pool"] = Pool(pipelinePool);
        if (model.Extends is { } extends) attributes["extends"] = extends.Reference;
        return attributes;
    }

    private static Dictionary<string, object?> StageAttributes(PipelineStage stage)
    {
        var attributes = Element(stage.DisplayName, stage.Execution, stage.Template, stage.Gate, stage.IsImplicit);
        if (stage.Name.Length > 0) attributes["name"] = stage.Name;
        if (stage.DependsOnDeclared) attributes["dependsOn"] = stage.DependsOn.Cast<object?>().ToList();
        if (stage.TriggerIsManual) attributes["manualTrigger"] = true;
        if (stage.IsSkippable.Length > 0) attributes["isSkippable"] = stage.IsSkippable;
        if (stage.Pool.Origin == PipelinePoolOrigin.Stage) attributes["ownPool"] = Pool(stage.Pool);
        return attributes;
    }

    private static Dictionary<string, object?> JobAttributes(PipelineJob job)
    {
        var attributes = Element(job.DisplayName, job.Execution, job.Template, job.Gate, job.IsImplicit);
        if (job.Name.Length > 0) attributes["name"] = job.Name;
        if (job.DependsOnDeclared) attributes["dependsOn"] = job.DependsOn.Cast<object?>().ToList();
        if (job.Strategy.Kind != PipelineStrategyKind.None)
        {
            attributes["strategy"] = Key(job.Strategy.Kind.ToString());
            attributes["multiplicity"] = (long)job.Strategy.Multiplicity;
            if (job.Strategy.MultiplicityExpression.Length > 0) attributes["multiplicityExpression"] = job.Strategy.MultiplicityExpression;
        }

        if (job.Pool.Origin == PipelinePoolOrigin.Job) attributes["ownPool"] = Pool(job.Pool);
        if (job is { IsDeployment: true, Environment.Length: > 0 }) attributes["environment"] = job.Environment;
        return attributes;
    }

    private static Dictionary<string, object?> StepAttributes(PipelineStep step)
    {
        var attributes = Element(step.DisplayName, step.Execution, step.Template, gate: "", isImplicit: false);
        attributes["stepKind"] = step.Kind.ToString().ToLowerInvariant();
        if (step.Identifier.Length > 0) attributes["identifier"] = step.Identifier;
        if (step.Hook.Length > 0) attributes["hook"] = step.Hook;
        return attributes;
    }

    /// <summary>A template reference that was not followed: what it names, where, and why it was not read.</summary>
    private static Dictionary<string, object?> TemplateAttributes(PipelineTemplateUnresolved unresolved, Dictionary<string, string> firstReadId)
    {
        var reference = unresolved.Reference;
        var attributes = Attributes(
            ("path", reference.Path),
            ("slot", Key(reference.Slot.ToString())),
            ("parameterNames", reference.ParameterNames.Cast<object?>().ToList()),
            ("reason", Key(unresolved.Reason.ToString())),
            ("ordinal", long.Parse(reference.Id["template-".Length..], System.Globalization.CultureInfo.InvariantCulture)));
        if (reference.Resource.Length > 0) attributes["resource"] = reference.Resource;
        if (reference.OwnerId.Length > 0 && firstReadId.TryGetValue(reference.OwnerId, out var owner)) attributes["referencedFrom"] = owner;
        return attributes;
    }

    /// <summary>What stages, jobs and steps share: the execution keys the file writes, and where the element came from.</summary>
    private static Dictionary<string, object?> Element(string displayName, PipelineExecution execution, string template, string gate, bool isImplicit)
    {
        var attributes = Attributes(("displayName", displayName));
        if (execution.Has(PipelineExecution.ConditionKey)) attributes["condition"] = execution.Condition;
        if (execution.Has(PipelineExecution.ContinueOnErrorKey)) attributes["continueOnError"] = execution.ContinueOnError;
        if (execution.Has(PipelineExecution.EnabledKey)) attributes["enabled"] = execution.Enabled;
        if (execution.Has(PipelineExecution.TimeoutKey)) attributes["timeoutInMinutes"] = execution.TimeoutInMinutes;
        if (template.Length > 0) attributes["template"] = template;
        if (gate.Length > 0) attributes["gate"] = gate;
        if (isImplicit) attributes["isImplicit"] = true;
        return attributes;
    }

    private static CelMap Pool(PipelinePool pool)
    {
        var map = new CelMap();
        if (pool.Name.Length > 0) map["name"] = pool.Name;
        if (pool.VmImage.Length > 0) map["vmImage"] = pool.VmImage;
        if (pool.Demands.Count > 0) map["demands"] = pool.Demands.Cast<object?>().ToList();
        return map;
    }

    /// <summary>An enum value's key: its C# name with the first letter lowered (<c>RunOnce</c> is <c>runOnce</c>).</summary>
    private static string Key(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] values)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in values)
        {
            attributes[name] = value;
        }

        return attributes;
    }
}
