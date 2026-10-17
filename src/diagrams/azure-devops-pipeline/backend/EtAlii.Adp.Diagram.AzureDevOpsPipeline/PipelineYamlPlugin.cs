using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The FBL 11 persistence plugin of the binding <c>azure-devops-pipeline.fbl#pipeline</c>
/// (<c>net.etalii.adp.azure-devops.pipelineYaml</c>): it reads a pipeline with <see cref="PipelineParser"/>,
/// following its templates through the <see cref="PipelineReadContext"/> it is constructed with, and hands
/// back the binding types the draft declared binding names, so the definition's <c>typeMap</c> holds under
/// either reader.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is constructed per body</b>, as <c>AbmBody</c> constructs its own, because what it is handed beside
/// the contract (the body's path and the workspace's resolver) is the body's. It keeps the
/// <see cref="PipelineModel"/> it read last, which the canvas, the layout and the commands address.
/// </para>
/// <para>
/// <b>The reading is the parser's order</b>: the pipeline, then each stage with its jobs and their steps as
/// written, followed templates in place, so <c>positionIn</c> counts as the parser's counters do and the
/// definition's derived ids (a stage's name, <c>stage/job</c>, <c>job/step-n</c>, <c>template:template-n</c>)
/// are the parser's ids. Then the template references not followed, then one <c>DependsOn</c> per name a stage
/// or a job writes: its target the element that waits, its source the first of that name without regard to
/// case, among the stages or among the jobs of the same stage, or unset (finding B6). Each element is read
/// under an id of its own place in the reading, because the parser's ids are not unique where two stages
/// share a name (finding A8): the first is the one a gesture finds, and a job is still beneath its own stage.
/// </para>
/// <para>
/// <b>A value is the text it was written with</b> (finding B1): <c>1.0</c>, <c>yes</c> and <c>null</c> are
/// strings, and no expression is evaluated. A value is present only where the file writes it, so
/// <c>has()</c> tells an absent key from an empty one.
/// </para>
/// <para>
/// <b>A body that is not YAML</b> is one finding, <c>azure-pipeline.unparseable</c>, on the line of the
/// problem with the reader's sentence, and the reading is unreadable: no element, and nothing is ever
/// written to it. Because YamlDotNet still reads, findings B14 and B15 do not arise.
/// </para>
/// <para>
/// <b>It does not plan yet</b>: a pipeline is still written by <see cref="PipelineWriter"/>
/// (azure-devops-pipeline-disl-fbl task 8), so <see cref="Plan"/> refuses every change.
/// </para>
/// </remarks>
internal sealed class PipelineYamlPlugin(PipelineReadContext context) : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    public const string PluginId = "net.etalii.adp.azure-devops.pipelineYaml";

    /// <summary>What the parser's id of a numbered template reference starts with: <c>template-0</c>.</summary>
    private const string TemplateIdPrefix = "template-";

    /// <summary>The name of a finding's <see cref="Finding.Detail"/> that holds the reader's own message.</summary>
    public const string ReasonDetail = "reason";

    /// <inheritdoc />
    public string Id => PluginId;

    /// <summary>The model the last <see cref="Read"/> read: the empty model for a body that is not YAML.</summary>
    public PipelineModel Model { get; private set; } = PipelineModel.Empty;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = LineDocument.Parse(Encoding.UTF8.GetString(Body(request.Files)));
        var outcome = PipelineParser.Outcome(document, context.Templates, context.BodyPath);

        // A reading of its own, never the one shared empty model, so the body that read it is found from it.
        Model = outcome.Model with { };
        if (!outcome.IsUsable)
        {
            var problem = outcome.Problems[0];
            var finding = new Finding(
                PipelineValidator.UnparseableRuleId,
                FindingSeverity.Error,
                $"This is not YAML that can be read: {problem.Message}",
                new SourceLocation("", problem.Line + 1, 1, 0))
            {
                Detail = new Dictionary<string, string>(StringComparer.Ordinal) { [ReasonDetail] = problem.Message },
            };
            return new PluginReadResult([], [finding], true);
        }

        return new PluginReadResult(Elements(Model, followedTemplates: context.Templates is not null), [], false);
    }

    /// <inheritdoc />
    /// <remarks>Refused: a pipeline is still written by <see cref="PipelineWriter"/>, not through its binding.</remarks>
    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PluginPlanResult.Refused("A pipeline is written by the module's writer, not yet through its binding.");
    }

    /// <inheritdoc />
    /// <remarks>Not asked, because the binding has <c>template.text</c>; it gives the same bytes.</remarks>
    public byte[] Template(PluginTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encoding.UTF8.GetBytes(PipelineBody.NewDocument(IoPath.GetFileNameWithoutExtension(request.Name)));
    }

    /// <inheritdoc />
    /// <remarks>None: reloading on a changed template stays the store's, as it is today.</remarks>
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    private static byte[] Body(IReadOnlyList<PluginFile> files) =>
        files.FirstOrDefault(file => file.RelativePath.Length == 0)?.Bytes ?? throw new ArgumentException("A pipeline is a file body.", nameof(files));

    /// <summary>The reading as the binding's types, in the parser's order.</summary>
    /// <param name="model">What the parser read.</param>
    /// <param name="followedTemplates">
    /// Whether the reading followed the templates it could: then the references not followed are the ones it
    /// gives a reason for. A reading that follows nothing, as the validator's, follows none of its references,
    /// and gives no reason because it tried none.
    /// </param>
    private static IReadOnlyList<FblElement> Elements(PipelineModel model, bool followedTemplates)
    {
        var nodes = new List<FblElement> { Node("pipeline", "Pipeline", PipelineAttributes(model), null) };
        var relations = new List<FblElement>();
        var firstReadId = new Dictionary<string, string>(StringComparer.Ordinal);
        var next = 0;

        var stages = new List<(PipelineStage Stage, string Read, List<(PipelineJob Job, string Read)> Jobs)>();
        foreach (var stage in model.Stages)
        {
            var stageRead = Place(stage.IsImplicit ? "ImplicitStage" : "Stage", StageAttributes(stage), null, stage.Id, LineOf(stage.IsImplicit, stage.Template, stage.Lines));
            var jobs = new List<(PipelineJob Job, string Read)>();
            foreach (var job in stage.Jobs)
            {
                var type = job.IsImplicit ? "ImplicitJob" : job.IsDeployment ? "DeploymentJob" : "Job";
                var jobRead = Place(type, JobAttributes(job), stageRead, job.Id, LineOf(job.IsImplicit, job.Template, job.Lines));
                jobs.Add((job, jobRead));
                foreach (var step in job.Steps)
                {
                    Place("Step", StepAttributes(step), jobRead, step.Id, LineOf(false, step.Template, step.Lines));
                }
            }

            stages.Add((stage, stageRead, jobs));
        }

        IEnumerable<(PipelineTemplateReference Reference, PipelineTemplateUnresolvedReason Reason)> unfollowed = followedTemplates
            ? model.Unresolved.Select(unresolved => (unresolved.Reference, unresolved.Reason))
            : model.Templates.Select(reference => (reference, PipelineTemplateUnresolvedReason.None));
        foreach ((PipelineTemplateReference reference, PipelineTemplateUnresolvedReason reason) in unfollowed)
        {
            nodes.Add(Node(NextId(), "TemplateReference", TemplateAttributes(reference, reason, firstReadId, model.Templates.Count), null));
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

        string Place(string type, IReadOnlyDictionary<string, object?> attributes, string? parent, string parsedId, int line)
        {
            var id = NextId();
            nodes.Add(new FblElement(id, false, type, type, false, attributes, parent, parent is null ? null : "children", null, null, default, line));
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

    /// <summary>
    /// The one-based line an element written in the body starts on; none for one nobody wrote or one a
    /// followed template holds, whose lines are the template's.
    /// </summary>
    private static int LineOf(bool isImplicit, string template, LineRange lines) =>
        isImplicit || template.Length > 0 ? 0 : lines.Start + 1;

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

    /// <summary>
    /// A template reference that was not followed: what it names, where, and why it was not read, where the
    /// reading says. Its parameters are read by name only, never by value.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="reason">Why it was not followed, or none for a reading that follows nothing.</param>
    /// <param name="firstReadId">The id each parsed element is read under.</param>
    /// <param name="unnumbered">
    /// The ordinal of an <c>extends</c> that was not followed: the parser numbers only the references under
    /// <c>stages</c>, <c>jobs</c> and <c>steps</c>, and calls this one <c>extends</c>, which the definition's id rule
    /// cannot name, so it takes the number after the last of theirs.
    /// </param>
    private static Dictionary<string, object?> TemplateAttributes(PipelineTemplateReference reference, PipelineTemplateUnresolvedReason reason, Dictionary<string, string> firstReadId, int unnumbered)
    {
        var ordinal = reference.Id.StartsWith(TemplateIdPrefix, StringComparison.Ordinal)
            ? long.Parse(reference.Id[TemplateIdPrefix.Length..], CultureInfo.InvariantCulture)
            : unnumbered;
        var attributes = Attributes(
            ("path", reference.Path),
            ("slot", Key(reference.Slot.ToString())),
            ("parameterNames", reference.ParameterNames.Cast<object?>().ToList()),
            ("ordinal", ordinal));
        if (reason != PipelineTemplateUnresolvedReason.None) attributes["reason"] = Key(reason.ToString());
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
