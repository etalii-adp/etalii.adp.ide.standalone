using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The Azure DevOps pipeline's bundled DISL definition (<c>definition/azure-devops-pipeline.dis</c>, from
/// etalii-adp/etalii.adp), loaded once as a <see cref="DislDiagramDefinition"/>, and what the providers
/// derive from it: the palette, the context menus and the property rows, mapped onto the host's types
/// with the wire ids of its <c>x-azure-pipeline</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the reading's own</b>: <see cref="PipelineBody.Disl"/>, built once by
/// <see cref="DislModelBuilder"/> from what the persistence plugin read, so a menu and a row read the
/// same snapshot. An entry's body is the one that read its model (<see cref="PipelineBody.ReadingOf"/>).
/// </para>
/// <para>
/// <b>An element is found by its id as the parser gives it</b>, a path through the file: the first
/// stage, job or step with it in written order, as <see cref="PipelineEdits.Locate"/> finds it. A
/// template reference is drawn but is not an element a menu or a form is offered for.
/// </para>
/// <para>
/// <b>Whether a stage or a job is open is the asking connection's</b>
/// (<see cref="PipelineConnectionView"/>), bound to <c>self.view.collapsed</c> for the one call, so
/// Show and Hide are one entry over it. <b>Read-only</b> (<c>env.readOnly</c>) is a file that could not
/// be read, which has no elements to offer anything for.
/// </para>
/// </remarks>
internal static class PipelineDefinition
{
    /// <summary>The definition, with its <c>x-azure-pipeline</c> wire ids.</summary>
    private static DislDiagramDefinition Definition { get; } = new(typeof(PipelineDefinition).Assembly, "azure-devops-pipeline.dis", "x-azure-pipeline");

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => Definition.Toolbox;

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>, open as <paramref name="view"/> has it; none for anything but a stage, a job or a step.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, PipelineModel> entry, PipelineConnectionView? view, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = DiagramOf(entry);
        return ElementOf(entry, diagram, elementId) is not { } element
            ? []
            : Viewed(diagram, view, () => Definition.Menus(DislMenuTarget.Element(element), Env));
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a stage's, a job's or a step's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, PipelineModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = DiagramOf(entry);
        return ElementOf(entry, diagram, elementId) is not { } element
            ? []
            : Viewed(diagram, null, () => Definition.Rows(element, Env));
    }

    /// <summary>
    /// The definition's findings for <paramref name="body"/>, after its reader's own: the six of the
    /// pipeline's rules in the order the definition declares them, none of them for a pipeline without a
    /// stage, or the one that the body is not YAML. The finding <see cref="DislModelBuilder"/> gives for an
    /// id computed twice stays with the model and is not one of them (Q3).
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(PipelineBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var diagram = body.Disl.Diagram;
        return Viewed(diagram, null, () => ConstraintEvaluator.Evaluate(Specification, diagram, new DislConstraintOptions(Env, body.ReaderFindings)));
    }

    /// <summary>
    /// Whether the definition's <c>view</c> action for <paramref name="actionId"/> on
    /// <paramref name="elementId"/> asks for the element to be opened or closed differently from how
    /// <paramref name="view"/> has it: the open-or-close entries are viewer state, which the host keeps.
    /// </summary>
    /// <returns>The element's id when it is to be toggled, else null; the refusal when the definition refuses.</returns>
    public static (string? Toggle, string? Refusal) View(DocumentEntry<LineDocument, PipelineModel> entry, PipelineConnectionView view, string elementId, string actionId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(view);
        var diagram = DiagramOf(entry);
        if (ElementOf(entry, diagram, elementId) is not { } element || !ViewOperations.Value.TryGetValue(actionId, out var operation))
        {
            return (null, null);
        }

        var transaction = Viewed(diagram, view, () => OperationInterpreter.Run(Specification, operation, diagram, element, DislIds.Fixed(), env: Env));
        if (!transaction.WasApplied) return (null, transaction.Refusal);

        var toggle = transaction.HostActions
            .OfType<HostAction.View>()
            .Any(action => action.Values.TryGetValue("collapsed", out var collapsed) && collapsed is bool closed && closed == view.IsExpanded(action.ElementId));
        return (toggle ? element.Id : null, null);
    }

    /// <summary>Whether <paramref name="actionId"/> is one the definition runs as viewer state.</summary>
    public static bool IsView(string actionId) => ViewOperations.Value.ContainsKey(actionId);

    /// <summary>
    /// The operations whose actions are a <c>view</c>, by the wire id <c>x-azure-pipeline</c> gives them:
    /// <c>azure-pipeline.toggle-stage</c> runs <c>toggleJobs</c>.
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> ViewOperations = new(() =>
    {
        var operations = Specification.Root.GetProperty("behavior").GetProperty("operations");
        return Specification.Root.GetProperty("x-azure-pipeline").GetProperty("actions").EnumerateObject()
            .Where(action => operations.TryGetProperty(action.Name, out var declared)
                && declared.TryGetProperty("actions", out var actions)
                && actions.EnumerateArray().Any(each => each.TryGetProperty("view", out _)))
            .ToDictionary(action => action.Value.GetString()!, action => action.Name, StringComparer.Ordinal);
    });

    /// <summary>The DISL diagram of what <paramref name="entry"/> holds, read by the body that read its model.</summary>
    private static DislDiagram DiagramOf(DocumentEntry<LineDocument, PipelineModel> entry) => PipelineBody.ReadingOf(entry.Model).Disl.Diagram;

    /// <summary>
    /// Binds every stage's and job's <c>self.view.collapsed</c> to <paramref name="view"/>, closed where it
    /// has nothing open (the definition's <c>view.initial</c>), and runs <paramref name="read"/> while the
    /// binding holds: one reading is shared by every connection that has the pipeline open, so the binding
    /// is per call and under a lock.
    /// </summary>
    private static T Viewed<T>(DislDiagram diagram, PipelineConnectionView? view, Func<T> read)
    {
        lock (diagram)
        {
            foreach (var node in diagram.Nodes.Where(node => node.IsA("Stage") || node.IsA("JobBase")))
            {
                node.View = new CelMap { ["collapsed"] = view is null || !view.IsExpanded(node.Id) };
            }

            return read();
        }
    }

    /// <summary>The stage, job or step <paramref name="id"/> names, the first in written order; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id.Length == 0
            ? null
            : diagram.Nodes.FirstOrDefault(node => node.Id == id && (node.IsA("Stage") || node.IsA("JobBase") || node.IsA("Step")));
    }

    /// <summary>The element <paramref name="id"/> names in <paramref name="entry"/>: none in a file that could not be read, which has no elements.</summary>
    private static DislElement? ElementOf(DocumentEntry<LineDocument, PipelineModel> entry, DislDiagram diagram, string id) =>
        entry.IsUsable ? ElementOf(diagram, id) : null;

    /// <summary>What <c>env</c> reads: editable, because a file that could not be read offers nothing at all.</summary>
    private static DislEnv Env { get; } = new();
}
