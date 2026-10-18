using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

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
/// same snapshot. The entry is the body.
/// </para>
/// <para>
/// <b>An element is found by its id as the definition derives it</b>, a path through the file: the
/// first stage, job or step with it in written order. A template reference is drawn but is not an
/// element a menu or a form is offered for.
/// </para>
/// <para>
/// <b>Every edit is the definition's</b> (azure-devops-pipeline-disl-fbl task 10): an operation run by
/// <see cref="OperationInterpreter"/>, a form row committed by <see cref="FormCommit"/>, the deletion
/// policy, or a built-in reorder, each refused by the definition's change rules
/// (<see cref="GestureConstraintEvaluator"/>) before anything is planned, and written by
/// <see cref="Apply"/> as one edit of the body.
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
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<PipelineBody, PipelineModel> entry, PipelineConnectionView? view, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = DiagramOf(entry);
        return ElementOf(entry, diagram, elementId) is not { } element
            ? []
            : Viewed(diagram, view, () => Definition.Menus(DislMenuTarget.Element(element), Env));
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a stage's, a job's or a step's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<PipelineBody, PipelineModel> entry, string elementId)
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

    /// <summary>The definition's sentence for a change to an element the pipeline no longer holds (<c>std.notApplicable</c>).</summary>
    public static string Gone { get; } = Specification.Root.GetProperty("behavior").GetProperty("messages").GetProperty("std.notApplicable").GetString()!;

    /// <summary>The stage, job or step <paramref name="id"/> names in <paramref name="body"/>, the first in written order; null for none, and for a body that is not YAML.</summary>
    public static DislElement? ElementOf(PipelineBody body, string id)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(id);
        return body.IsReadOnly ? null : ElementOf(body.Disl.Diagram, id);
    }

    /// <summary>
    /// Runs the operation <paramref name="operationId"/> on <paramref name="self"/> in its diagram, which is the
    /// transaction's working state: an edit runs on a body of its own.
    /// </summary>
    /// <remarks>
    /// The ids an operation's <c>create</c>s are given are the add's own in the plan, never read: the binding's ids
    /// are the plugin's, and the definition derives the new elements' ids when the body is read again.
    /// </remarks>
    public static DislTransaction Run(string operationId, DislDiagram diagram, DislElement? self)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(diagram);
        return OperationInterpreter.Run(Specification, operationId, diagram, self, DislIds.Fixed(NewIds), env: Env);
    }

    /// <summary>
    /// Why the definition refuses changing <paramref name="attribute"/> of <paramref name="element"/> to
    /// <paramref name="newValue"/> (its change rules, DISL 8.4); null when it does not. Null unsets the attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first refusal in the order today's commands asked</b>: a step's dependencies, then an element this file
    /// does not write, then a name that names nothing, then a cycle, then a pipeline left with no stage to start
    /// with; the definition declares the read-only rule last, which would put its sentence behind the others.
    /// </para>
    /// <para>
    /// <b>The cycle and the starting stage are preconditions over <c>reachable</c></b>, in place of the definition's
    /// <c>noCycleByEdit</c> and <c>keepAStart</c>: as bundled, the first refuses a dependency on what the element
    /// already waits for and lets the one that closes a loop through, because <c>reachable</c> walks from what is
    /// waited for to what waits; the second refuses taking a stage back to the default order, which is no edge.
    /// </para>
    /// </remarks>
    public static string? ChangeRefusal(DislElement element, string attribute, object? newValue)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attribute);
        var refusals = GestureConstraintEvaluator.Change(Specification, element, attribute, newValue, Env)
            .Where(refusal => refusal.ConstraintId is not ("noCycleByEdit" or "keepAStart"))
            .ToList();
        return First("stepHasNothingToWaitFor") ?? First("readOnlyElements") ?? First("knownName") ?? First("knownStageName")
            ?? (attribute == "dependsOn" && newValue is IReadOnlyList<object?> names ? Waits(element, names) : null)
            ?? refusals.FirstOrDefault()?.Message;

        string? First(string id) => refusals.FirstOrDefault(refusal => refusal.ConstraintId == id)?.Message;
    }

    /// <summary>Why the definition refuses deleting <paramref name="element"/> (its deletion rules, DISL 8.4); null when it does not.</summary>
    /// <remarks>
    /// <para>
    /// <b>All but <c>keepTheOnlyStart</c></b>, which the remove command has never refused with: as bundled it refuses
    /// removing the first of stages that follow the default order, which leaves the next one to start with, and the
    /// menu offers that removal.
    /// </para>
    /// <para>
    /// <b>The last job and the last step are refused with today's sentence</b>, which calls a stage or a job without a
    /// display name or a name "stage" or "job": the definition's <c>label</c> is empty for one, which would make the
    /// sentence start with nothing.
    /// </para>
    /// </remarks>
    public static string? DeleteRefusal(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GestureConstraintEvaluator.Delete(Specification, element, env: Env).FirstOrDefault(refusal => refusal.ConstraintId != "keepTheOnlyStart") switch
        {
            null => null,
            { ConstraintId: "keepLastJob" } => $"{Label(element.Parent, "stage")} needs at least one job.",
            { ConstraintId: "keepLastStep" } => $"{Label(element.Parent, "job")} needs at least one step.",
            var refusal => refusal.Message,
        };
    }

    /// <summary>
    /// Why the definition refuses moving <paramref name="element"/> from <paramref name="oldIndex"/> to
    /// <paramref name="newIndex"/> among <paramref name="siblings"/> (its reorder rules, DISL 8.4); null when it does not.
    /// </summary>
    public static string? ReorderRefusal(DislElement element, IReadOnlyList<DislElement> siblings, int oldIndex, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GestureConstraintEvaluator.Reorder(Specification, element, siblings, oldIndex, newIndex, Env).FirstOrDefault()?.Message;
    }

    /// <summary>
    /// Why <paramref name="element"/>, a stage or a job, may not wait for <paramref name="names"/>: one of them already
    /// waits for it, or is it, or the pipeline would be left with no stage that waits for nothing.
    /// </summary>
    /// <remarks>A name is the first stage, or the first job of the same stage, of that name, compared without regard to case.</remarks>
    private static string? Waits(DislElement element, IReadOnlyList<object?> names)
    {
        var isStage = element.IsA("Stage");
        var candidates = isStage ? element.Diagram.NodesOfType("Stage") : element.Parent?.Children.Where(child => child.IsA("JobBase")).ToList() ?? [];
        var downstream = Reachable(element);
        foreach (var name in names.OfType<string>())
        {
            var named = candidates.FirstOrDefault(candidate => candidate.ValueOf("name") is string { Length: > 0 } written && string.Equals(written, name, StringComparison.OrdinalIgnoreCase));
            if (named is not null && (ReferenceEquals(named, element) || downstream.Contains(named)))
            {
                return $"That would make {Label(element, isStage ? "stage" : "job")} wait for something that is already waiting for it.";
            }
        }

        var starts = names.Count > 0 && isStage && !element.Diagram.NodesOfType("Stage").Any(stage => !ReferenceEquals(stage, element) && !stage.Incoming.Any(relation => relation.IsA("Dependency") && relation.Source is not null));
        return starts ? "A pipeline must contain at least one stage with no dependencies, or it has nothing to start with." : null;
    }

    /// <summary>What waits for <paramref name="element"/>, directly or not: the definition's <c>reachable('Dependency')</c>.</summary>
    private static HashSet<DislElement> Reachable(DislElement element) =>
        element.TryInvoke("reachable", ["Dependency"], out var reached) && reached is IEnumerable<object?> found
            ? found.OfType<DislElement>().ToHashSet<DislElement>(ReferenceEqualityComparer.Instance)
            : [];

    /// <summary>What a sentence calls <paramref name="element"/>: its display name, else its name, else <paramref name="fallback"/>.</summary>
    private static string Label(DislElement? element, string fallback) =>
        element?.ValueOf("displayName") is string { Length: > 0 } displayName ? displayName
        : element?.ValueOf("name") is string { Length: > 0 } name ? name
        : fallback;

    /// <summary>
    /// Writes the changes of <paramref name="transaction"/>, run in <paramref name="body"/>'s DISL model, into the
    /// body as one edit: each refused first by the definition's change rules, then planned by the plugin; the first
    /// refusal, the transaction's own, a rule's or the planner's, writes nothing.
    /// </summary>
    /// <remarks>
    /// Every id is written as the plugin read it (<see cref="PipelineBody.ReadIdOf"/>), but for an element an add
    /// of the same transaction made, which is named by the id its add gave.
    /// </remarks>
    public static EditOutcome Apply(PipelineBody body, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied) return EditOutcome.Refused(transaction.Refusal!);

        var diagram = body.Disl.Diagram;
        var created = transaction.Changes.OfType<DislChange.Create>().Select(create => create.Id).ToHashSet(StringComparer.Ordinal);
        var changes = new List<ModelChange>(transaction.Changes.Count);
        foreach (var change in transaction.Changes)
        {
            if (change is DislChange.Set set && ElementOf(diagram, set.ElementId) is { } element)
            {
                foreach ((string attribute, object? value) in set.Attributes)
                {
                    if (ChangeRefusal(element, attribute, value) is { } refused) return EditOutcome.Refused(refused);
                }
            }

            changes.Add(AsRead(DislWrite.ToFbl(Specification, change)));
        }

        return body.Write(changes);

        ModelChange AsRead(ModelChange change) => change switch
        {
            ModelChange.Add add => add with { ParentId = add.ParentId is { } parent ? Read(parent) : null },
            ModelChange.Set set => set with { Id = Read(set.Id) },
            ModelChange.Remove remove => new ModelChange.Remove(Read(remove.Id)),
            ModelChange.Move move => move with { Id = Read(move.Id), NewParentId = move.NewParentId is { } parent ? Read(parent) : null },
            _ => change,
        };

        string Read(string id) => created.Contains(id) ? id : body.ReadIdOf(id) ?? id;
    }

    /// <summary>The ids an operation's creates are given: a new stage is three (the stage, its job and the job's step).</summary>
    private static readonly string[] NewIds = ["added-0", "added-1", "added-2"];

    /// <summary>
    /// Whether the definition's <c>view</c> action for <paramref name="actionId"/> on
    /// <paramref name="elementId"/> asks for the element to be opened or closed differently from how
    /// <paramref name="view"/> has it: the open-or-close entries are viewer state, which the host keeps.
    /// </summary>
    /// <returns>The element's id when it is to be toggled, else null; the refusal when the definition refuses.</returns>
    public static (string? Toggle, string? Refusal) View(DocumentEntry<PipelineBody, PipelineModel> entry, PipelineConnectionView view, string elementId, string actionId)
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

    /// <summary>The DISL diagram of what <paramref name="entry"/> holds.</summary>
    private static DislDiagram DiagramOf(DocumentEntry<PipelineBody, PipelineModel> entry) => entry.Document.Disl.Diagram;

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
    private static DislElement? ElementOf(DocumentEntry<PipelineBody, PipelineModel> entry, DislDiagram diagram, string id) =>
        entry.IsUsable ? ElementOf(diagram, id) : null;

    /// <summary>What <c>env</c> reads: editable, because a file that could not be read offers nothing at all.</summary>
    private static DislEnv Env { get; } = new();
}
