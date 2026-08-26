using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Turns a parsed, laid-out pipeline into the core element vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Stages, jobs, steps, edges and unfollowed templates all travel as <see cref="DiagramElement"/>
/// with a pipeline payload in the contract's <c>Any</c>. Nothing in the core contract is extended
/// for this type - it is the fourth to ride that vocabulary, which is the point of Requirement 11.
/// </para>
/// <para>
/// Ids are the element's path within the document: the stage's name, then the job's, then the
/// step's position. The pipeline schema gives elements names but no ids and this module will not
/// add any to the file, so a path is what there is - and it is the right answer anyway, because it
/// survives an edit somewhere else in the file and a selection is therefore not lost on every
/// keystroke (Requirement 11.3).
/// </para>
/// </remarks>
public sealed class PipelineElementMapper
{
    /// <summary>The mime-style element kinds this module puts on the wire.</summary>
    public const string StageType = "azure-devops/pipeline+stage";

    /// <summary>A job, whether a plain one or a deployment.</summary>
    public const string JobType = "azure-devops/pipeline+job";

    /// <summary>One step of a job.</summary>
    public const string StepType = "azure-devops/pipeline+step";

    /// <summary>A dependency arrow.</summary>
    public const string EdgeType = "azure-devops/pipeline+edge";

    /// <summary>A template reference standing where a stage or job would.</summary>
    public const string TemplateType = "azure-devops/pipeline+template";

    private readonly PipelineMetrics _metrics;

    /// <summary>Creates a mapper arranging at <paramref name="metrics"/>.</summary>
    public PipelineElementMapper(PipelineMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Everything a connection sees of <paramref name="model"/>.
    /// </summary>
    /// <remarks>
    /// The viewport is accepted and deliberately not used to filter: these are small graphs, and
    /// answering with the whole pipeline is what Requirement 11.8 permits. Taking the parameter
    /// anyway keeps the seam where filtering would go, for the day a pipeline turns up that needs
    /// it.
    /// </remarks>
    /// <param name="model">The pipeline.</param>
    /// <param name="viewport">What the connection can see; unused, per Requirement 11.8.</param>
    /// <param name="expandedStageIds">Which stages are showing their jobs.</param>
    public IReadOnlyList<DiagramElement> Visible(
        PipelineModel model,
        DiagramViewport viewport,
        IReadOnlySet<string>? expandedStageIds = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _ = viewport;

        var expanded = expandedStageIds ?? new HashSet<string>(StringComparer.Ordinal);
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, expanded);
        var elements = new List<DiagramElement>();

        var stageGraph = PipelineGraphBuilder.OfStages(model);
        foreach (var stage in model.Stages)
        {
            var placement = arrangement.Of(stage.Id);
            if (placement is null)
            {
                continue;
            }

            elements.Add(StageElement(stage, placement, stageGraph));

            if (!expanded.Contains(stage.Id))
            {
                // A collapsed stage carries no children on the wire. What it contains has not
                // changed, only whether this connection is looking at it - which is what
                // Group/Ungroup says, and why the jobs are simply absent rather than marked.
                continue;
            }

            foreach (var job in stage.Jobs)
            {
                var jobPlacement = arrangement.Of(job.Id);
                if (jobPlacement is not null)
                {
                    elements.Add(JobElement(job, stage, jobPlacement));
                }
            }

            elements.AddRange(EdgesOf(PipelineGraphBuilder.OfJobs(stage), arrangement, stage.Id));
        }

        elements.AddRange(EdgesOf(stageGraph, arrangement, ""));
        elements.AddRange(UnresolvedTemplates(model, arrangement));
        return elements;
    }

    /// <summary>The steps of one job, which the canvas asks for when a job is opened.</summary>
    /// <remarks>
    /// Steps are a sequence and not a graph (Requirement 6.4), so they are laid out by counting
    /// rather than by the layout engine - there is nothing for it to decide.
    /// </remarks>
    public IReadOnlyList<DiagramElement> StepsOf(PipelineJob job, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.Steps
            .Select((step, index) => Element(
                step.Id,
                x,
                y + (index * (_metrics.JobHeight + _metrics.VerticalGap)),
                StepType,
                new PipelineElementPayload
                {
                    Name = "",
                    DisplayName = step.Label,
                    Kind = PipelineElementKindProto.PipelineElementKindStep,
                    Condition = step.Execution.Condition,
                    Indeterminate = false,
                    FromTemplate = step.IsFromTemplate,
                    TemplatePath = step.Template,
                    Enabled = !step.Execution.IsDisabled,
                    Multiplicity = 1,
                    Hook = step.Hook,
                    ParentId = job.Id,
                    Width = _metrics.JobWidth,
                    Height = _metrics.JobHeight,
                    FirstLine = step.Lines.Start + 1,
                    LastLine = step.Lines.End + 1,
                }))
            .ToList();
    }

    private DiagramElement StageElement(PipelineStage stage, PipelinePlacement placement, PipelineGraph graph)
    {
        // A stage is uncertain either because a compile-time expression decides whether it exists
        // at all, or because it waits on something that does not.
        var indeterminate = stage.Gate.Length > 0 ||
            graph.Edges.Any(edge => edge.ToId == stage.Id && edge.IsBroken);

        return Element(
            stage.Id,
            placement.X,
            placement.Y,
            StageType,
            new PipelineElementPayload
            {
                Name = stage.Name,
                DisplayName = stage.Label,
                Kind = PipelineElementKindProto.PipelineElementKindStage,
                Condition = stage.Execution.Condition,
                Indeterminate = indeterminate,
                FromTemplate = stage.IsFromTemplate,
                TemplatePath = stage.Template,
                Enabled = !stage.Execution.IsDisabled,
                Multiplicity = 1,
                Pool = stage.Pool.Label,
                PoolInherited = stage.Pool.Origin == PipelinePoolOrigin.Pipeline,
                Width = placement.Size.Width,
                Height = placement.Size.Height,
                // An implicit stage is not in the file, so there is no line to open.
                FirstLine = stage.IsImplicit ? 0 : stage.Lines.Start + 1,
                LastLine = stage.IsImplicit ? 0 : stage.Lines.End + 1,
            });
    }

    private DiagramElement JobElement(PipelineJob job, PipelineStage stage, PipelinePlacement placement) =>
        Element(
            job.Id,
            placement.X,
            placement.Y,
            JobType,
            new PipelineElementPayload
            {
                Name = job.Name,
                DisplayName = job.Label,
                Kind = job.IsDeployment
                    ? PipelineElementKindProto.PipelineElementKindDeploymentJob
                    : PipelineElementKindProto.PipelineElementKindJob,
                Condition = job.Execution.Condition,
                // A job whose count is decided at run time must not look like one job.
                Indeterminate = job.Gate.Length > 0 || job.Strategy.MultiplicityExpression.Length > 0,
                FromTemplate = job.IsFromTemplate,
                TemplatePath = job.Template,
                Enabled = !job.Execution.IsDisabled,
                Multiplicity = job.Strategy.Multiplicity,
                Pool = job.Pool.Label,
                PoolInherited = job.Pool.IsInheritedByAJob,
                Environment = job.Environment,
                Strategy = job.Strategy.Kind == PipelineStrategyKind.None ? "" : job.Strategy.Kind.ToString(),
                ParentId = stage.Id,
                Width = placement.Size.Width,
                Height = placement.Size.Height,
                FirstLine = job.IsImplicit ? 0 : job.Lines.Start + 1,
                LastLine = job.IsImplicit ? 0 : job.Lines.End + 1,
            });

    /// <summary>
    /// The arrows of one level.
    /// </summary>
    /// <remarks>
    /// An edge is an element of its own naming its two endpoints, as the mindmap and Wardley
    /// modules established. Its id has to be stable for the same reason every other id does, so it
    /// is built from the two ends rather than from a counter - a counter would renumber every
    /// arrow the moment one was added.
    /// </remarks>
    private IEnumerable<DiagramElement> EdgesOf(PipelineGraph graph, PipelineArrangement arrangement, string parentId) =>
        graph.Edges.Select(edge => Element(
            EdgeId(edge),
            arrangement.Of(edge.ToId)?.X ?? 0,
            arrangement.Of(edge.ToId)?.Y ?? 0,
            EdgeType,
            new PipelineElementPayload
            {
                Name = edge.FromName,
                DisplayName = "",
                Kind = PipelineElementKindProto.PipelineElementKindEdge,
                Condition = edge.ConditionText,
                Indeterminate = edge.IsBroken,
                Enabled = true,
                Multiplicity = 1,
                SourceId = edge.FromId,
                TargetId = edge.ToId,
                EdgeCondition = Wire(edge.Condition),
                ImplicitDependency = edge.IsImplicit,
                Broken = edge.IsBroken,
                ParentId = parentId,
            }));

    /// <summary>
    /// The templates that could not be followed, each drawn where its reference stood.
    /// </summary>
    /// <remarks>
    /// A followed template contributes its elements and needs no element of its own - the reader
    /// sees what it brought, each marked with where it came from. One that could not be followed
    /// has nothing to show but itself, and showing it is the whole of Requirement 5.3: a diagram
    /// that quietly drops a template is worse than one that admits the gap.
    /// </remarks>
    private IEnumerable<DiagramElement> UnresolvedTemplates(PipelineModel model, PipelineArrangement arrangement)
    {
        var below = arrangement.Size.Height + _metrics.VerticalGap;
        return model.Unresolved.Select((unresolved, index) => Element(
            $"template:{unresolved.Reference.Id}",
            arrangement.Of(unresolved.Reference.OwnerId)?.X ?? 0,
            below + (index * (_metrics.StageHeight + _metrics.VerticalGap)),
            TemplateType,
            new PipelineElementPayload
            {
                Name = unresolved.Reference.Path,
                DisplayName = unresolved.Reference.Reference,
                Kind = PipelineElementKindProto.PipelineElementKindTemplate,
                Indeterminate = true,
                Enabled = true,
                Multiplicity = 1,
                Reference = unresolved.Reference.Reference,
                UnresolvedReason = unresolved.Explanation,
                ParentId = unresolved.Reference.OwnerId,
                Width = _metrics.StageWidth,
                Height = _metrics.StageHeight,
                FirstLine = unresolved.Reference.Lines.Start + 1,
                LastLine = unresolved.Reference.Lines.End + 1,
            }));
    }

    /// <summary>
    /// An edge's id, built from the pair it joins.
    /// </summary>
    /// <remarks>
    /// A broken edge has no source id, so the name it failed to match stands in - two different
    /// dangling names into one element are two different mistakes and want two different arrows.
    /// </remarks>
    public static string EdgeId(PipelineEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var from = edge.FromId.Length > 0 ? edge.FromId : $"?{edge.FromName}";
        return $"edge:{from}->{edge.ToId}";
    }

    private static PipelineEdgeConditionProto Wire(PipelineEdgeCondition condition) => condition switch
    {
        PipelineEdgeCondition.Always => PipelineEdgeConditionProto.PipelineEdgeConditionAlways,
        PipelineEdgeCondition.OnFailure => PipelineEdgeConditionProto.PipelineEdgeConditionOnFailure,
        PipelineEdgeCondition.OnSuccessOrFailure => PipelineEdgeConditionProto.PipelineEdgeConditionOnSuccessOrFailure,
        PipelineEdgeCondition.Custom => PipelineEdgeConditionProto.PipelineEdgeConditionCustom,
        _ => PipelineEdgeConditionProto.PipelineEdgeConditionOnSuccess,
    };

    private static DiagramElement Element(string id, double x, double y, string type, PipelineElementPayload payload) =>
        new(id, x, y, type, PayloadTypeUrl, payload.ToByteArray());

    /// <summary>The type URL a protobuf <c>Any</c> carries for this module's payload.</summary>
    public static string PayloadTypeUrl => "type.googleapis.com/" + PipelineElementPayload.Descriptor.FullName;
}
