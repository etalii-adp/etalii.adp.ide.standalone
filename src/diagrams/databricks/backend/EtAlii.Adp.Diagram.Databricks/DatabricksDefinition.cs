using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The three bundled DISL definitions of the family (<c>definition/databricks-bundle.dis</c>, <c>databricks-job.dis</c>
/// and <c>databricks-pipeline.dis</c>, from etalii-adp/etalii.adp), each loaded once as a
/// <see cref="DislDiagramDefinition"/>, and what the providers derive from them: the palettes, the context menus, the
/// property rows with their read-only reasons, the removal's confirmation and a dropped task's key, mapped onto the
/// host's types with the wire ids of each definition's <c>x-databricks</c> block (databricks-disl-fbl Requirement 3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is a reading of the parsed file</b> (<see cref="DatabricksDisl"/>), of the resource the target's
/// registration names (<see cref="DatabricksSelection.ReadingOf"/>, finding B3): a job's, the bundle's or a pipeline's.
/// </para>
/// <para>
/// <b>An element is found in the reading that holds it</b>: the first job of the reading that has an element of the
/// id, else the bundle, else the first pipeline that has one - which is where the module's selection finds a task, a
/// dependency, the bundle, a target, a library or the pipeline. The three readings' ids do not overlap, so an id names
/// its reading whichever tool type the target was opened as. <b>The empty canvas and a connect gesture</b> are the
/// reading the target's tool type opens (the job's when it names none), whose first job, bundle or first pipeline they
/// are on.
/// </para>
/// <para>
/// <b>The menus are derived editable</b>, and none is offered on a file that does not parse: no definition says
/// anything of a read-only diagram, and the hand-written menus withheld every edit there. The rows are derived editable
/// too, also on such a file, as they always were.
/// </para>
/// <para>
/// <b>What an entry does stays the providers'</b>: an entry's id and arguments choose the module's existing command,
/// whose records and handlers do not change in stage one. The three simulated entries are derived for their label and
/// id only, and answered as a no-op by the <see cref="DatabricksContextActionProvider.SimulatedMarker"/>.
/// </para>
/// </remarks>
internal static class DatabricksDefinition
{
    /// <summary>The block of every definition that holds the family's wire ids.</summary>
    private const string WireBlock = "x-databricks";

    /// <summary>The bundle's definition.</summary>
    private static DislDiagramDefinition Bundle { get; } = new(typeof(DatabricksDefinition).Assembly, "databricks-bundle.dis", WireBlock);

    /// <summary>The job's definition, with the free-key function its add operations call (<see cref="DatabricksFreeKey"/>).</summary>
    private static DislDiagramDefinition Job { get; } = new(typeof(DatabricksDefinition).Assembly, "databricks-job.dis", WireBlock, DatabricksFreeKey.Plugins);

    /// <summary>The pipeline's definition.</summary>
    private static DislDiagramDefinition Pipeline { get; } = new(typeof(DatabricksDefinition).Assembly, "databricks-pipeline.dis", WireBlock);

    /// <summary>What <c>env</c> reads: editable, in the one viewpoint each reading has.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The definition of the tool type <paramref name="origin"/> names: the job's when it names none.</summary>
    private static DislDiagramDefinition Of(ToolOrigin? origin) => origin?.Type switch
    {
        "bundle" => Bundle,
        "pipeline" => Pipeline,
        _ => Job,
    };

    /// <summary>The palette of the tool type <paramref name="origin"/> names.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox(ToolOrigin origin) => Of(origin).Toolbox;

    /// <summary>
    /// The context menu of <paramref name="target"/> in <paramref name="entry"/>: an element's, the empty canvas's at a
    /// placement, a connect gesture's; none for anything else, and none on a file that does not parse.
    /// </summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable)
        {
            return [];
        }

        var reading = DatabricksSelection.ReadingOf(entry.Model, target);
        if (ElementOf(reading, target.ElementId) is ({ } definition, { } element))
        {
            return definition.Menus(DislMenuTarget.Element(element), Env);
        }

        if (DatabricksNewPlacement.IsPlacement(target.ElementId))
        {
            return Of(target.Origin).Menus(DislMenuTarget.Canvas(CanvasOf(target.Origin, reading)), Env);
        }

        if (DatabricksRelationGesture.TryParse(target.ElementId, out var from, out var to))
        {
            var diagram = CanvasOf(target.Origin, reading);
            return Of(target.Origin).Menus(DislMenuTarget.Connection(diagram, DatabricksDisl.ElementOf(diagram, from), DatabricksDisl.ElementOf(diagram, to)), Env);
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="target"/> in <paramref name="entry"/>: an element's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(DatabricksSelection.ReadingOf(entry.Model, target), target.ElementId) is ({ } definition, { } element)
            ? definition.Rows(element, Env)
            : [];
    }

    /// <summary>What removing the element <paramref name="target"/> names in <paramref name="model"/> asks first; null when it asks nothing.</summary>
    public static RemovalConfirmation? RemoveConfirmation(ContextTarget target, DatabricksModel model)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(DatabricksSelection.ReadingOf(model, target), target.ElementId) is ({ } definition, { Type.IsRelation: false } element)
            && DeletionPolicy.Confirmation(definition.Specification, element, env: Env) is { } asked
                ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
                : null;
    }

    /// <summary>
    /// The key of a task dropped on the empty canvas of <paramref name="job"/> by <paramref name="actionId"/>: the
    /// <c>key</c> argument of the job definition's tool that drops it, its drop target's <c>freeTaskKey</c> (finding
    /// A11); for an id no tool drops, the same first free key of the type the id names.
    /// </summary>
    public static string DroppedTaskKey(JobModel job, string actionId)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(actionId);

        var diagram = DatabricksDisl.Job(job, Job.Specification).Diagram;
        var tool = ToolboxDerivation.Derive(Job.Specification, Job.Ids).FirstOrDefault(candidate => candidate.DropActionId == actionId);
        if (tool is not null
            && DislDrop.Resolve(Job.Specification, tool.Name, diagram, null, Env) is DislDropOutcome.Target landed
            && landed.Arguments.TryGetValue("key", out var key)
            && key is string text)
        {
            return text;
        }

        var type = actionId[(actionId.LastIndexOf(':') + 1)..];
        return DatabricksFreeKey.Of([.. diagram.NodesOfType("Task").Select(task => task.ValueOf("key") as string ?? "")], type);
    }

    /// <summary>
    /// The definition and the element <paramref name="elementId"/> names in <paramref name="reading"/>: of its first job
    /// that has one, else of its bundle, else of its first pipeline that has one; nothing for none.
    /// </summary>
    private static (DislDiagramDefinition? Definition, DislElement? Element) ElementOf(DatabricksModel reading, string? elementId)
    {
        if (elementId is not { Length: > 0 })
        {
            return (null, null);
        }

        foreach (var job in reading.Jobs)
        {
            if (DatabricksDisl.ElementOf(DatabricksDisl.Job(job, Job.Specification).Diagram, elementId) is { } task)
            {
                return (Job, task);
            }
        }

        if (DatabricksDisl.ElementOf(DatabricksDisl.Bundle(reading.Bundle, Bundle.Specification).Diagram, elementId) is { } bundle)
        {
            return (Bundle, bundle);
        }

        foreach (var pipeline in reading.Pipelines)
        {
            if (DatabricksDisl.ElementOf(DatabricksDisl.Pipeline(pipeline, Pipeline.Specification).Diagram, elementId) is { } element)
            {
                return (Pipeline, element);
            }
        }

        return (null, null);
    }

    /// <summary>The diagram the empty canvas of the tool type <paramref name="origin"/> names is: the first job's, the bundle's or the first pipeline's of <paramref name="reading"/>.</summary>
    private static DislDiagram CanvasOf(ToolOrigin? origin, DatabricksModel reading) => origin?.Type switch
    {
        "bundle" => DatabricksDisl.Bundle(reading.Bundle, Bundle.Specification).Diagram,
        "pipeline" => DatabricksDisl.Pipeline(reading.Pipelines.FirstOrDefault(), Pipeline.Specification).Diagram,
        _ => DatabricksDisl.Job(reading.Jobs.FirstOrDefault(), Job.Specification).Diagram,
    };
}
