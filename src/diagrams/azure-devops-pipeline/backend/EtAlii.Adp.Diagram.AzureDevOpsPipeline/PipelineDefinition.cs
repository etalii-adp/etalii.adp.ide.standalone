using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
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
/// <b>The model is the reading's own</b>: <see cref="PipelineDisl.ModelOf"/>, built once from the
/// parser's <see cref="PipelineModel"/>, so a menu and a row read the same snapshot.
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
        var diagram = PipelineDisl.ModelOf(entry.Model);
        return ElementOf(entry, diagram, elementId) is not { } element
            ? []
            : PipelineDisl.Viewed(diagram, view, () => Definition.Menus(DislMenuTarget.Element(element), Env));
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a stage's, a job's or a step's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, PipelineModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = PipelineDisl.ModelOf(entry.Model);
        return ElementOf(entry, diagram, elementId) is not { } element
            ? []
            : PipelineDisl.Viewed(diagram, null, () => Definition.Rows(element, Env));
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
        var diagram = PipelineDisl.ModelOf(entry.Model);
        if (ElementOf(entry, diagram, elementId) is not { } element || !ViewOperations.Value.TryGetValue(actionId, out var operation))
        {
            return (null, null);
        }

        var transaction = PipelineDisl.Viewed(diagram, view, () => OperationInterpreter.Run(Specification, operation, diagram, element, DislIds.Fixed(), env: Env));
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
