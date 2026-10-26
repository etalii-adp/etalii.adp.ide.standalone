using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The supply chain diagram's bundled DISL definition (<c>definition/supply-chain.dis</c>, from etalii-adp/etalii.adp),
/// loaded once as a <see cref="DislDiagramDefinition"/> with <see cref="SupplyChainWritten"/>, and what the providers
/// derive from it: the palette, the context menus, the property rows, what removing a node asks first and the findings.
/// </summary>
/// <remarks>
/// <para>
/// <b>An element is found by the id it is written with</b>, as <see cref="SupplyChainModel"/> finds it: the first node
/// with it, else the first flow, else the first group. The empty id finds the first entry written without one, as the model does; an id nothing is written with has no menu.
/// </para>
/// <para>
/// <b>Arrange and the two decreases carry their sentence even while they are available</b>, as the hand-written
/// menus did.
/// </para>
/// </remarks>
internal static class SupplyChainDefinition
{
    /// <summary>The definition, with its <c>x-supply-chain</c> wire ids; core's <see cref="DislToolboxProvider"/> hands out its palette.</summary>
    internal static DislDiagramDefinition Definition { get; } = new(
        typeof(SupplyChainDefinition).Assembly,
        "supply-chain.dis",
        "x-supply-chain",
        SupplyChainWritten.Plugins,
        ["arrange", "decreaseQuantity", "decreaseVolume"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => Definition.Toolbox;

    /// <summary>What <c>env</c> reads for a menu, a form and an edit: editable, in no viewpoint.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="model"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = Diagram(model);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, NodeOf(diagram, from), NodeOf(diagram, to), "Flow")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="model"/>: a node's, a flow's or a group's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(Diagram(model), elementId) is { } element ? Definition.Rows(element, Env) : [];
    }

    /// <summary>What removing <paramref name="elementId"/> asks first, the definition's <c>behavior.deletion</c>: a node with a flow written to or from it; none otherwise.</summary>
    public static RemovalConfirmation? RemovalConfirmation(SupplyChainModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(Diagram(model), elementId) is { } element && DeletionPolicy.Confirmation(Specification, element, env: Env) is { } asked
            ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
            : null;
    }

    /// <summary>
    /// The findings of <paramref name="model"/>: the definition's constraints (<see cref="ConstraintEvaluator"/>) with what the
    /// reading could not read handed in as the reader's findings, an element named by the id it is written with, in one list
    /// by line.
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var options = new DislConstraintOptions(Env, SupplyChainReaderFindings.Of(model), SupplyChainWritten.IdOf);
        return SupplyChainFindings.ByLine(ConstraintEvaluator.Evaluate(Specification, Diagram(model), options));
    }

    /// <summary>
    /// Runs the operation <paramref name="operation"/> on the element <paramref name="selfId"/> names (the diagram for
    /// null) over a model of <paramref name="model"/> of its own, and writes what it changes into
    /// <paramref name="document"/>; or its refusal.
    /// </summary>
    public static EditOutcome Run(LineDocument document, SupplyChainModel model, string operation, string? selfId, IIdSource ids, DislInvocation? invocation = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var working = SupplyChainReading.BodyOf(model).Working().Diagram;
        var self = selfId is null ? null : ElementOf(working, selfId);
        return Apply(document, model, OperationInterpreter.Run(Specification, operation, working, self, ids, invocation, Env));
    }

    /// <summary>
    /// Commits <paramref name="value"/> to the row <paramref name="row"/> of <paramref name="self"/>'s form (DISL 0.3 §7.5),
    /// over a model of <paramref name="model"/> of its own, and writes what it changes into <paramref name="document"/>;
    /// or its refusal.
    /// </summary>
    private static EditOutcome Commit(LineDocument document, SupplyChainModel model, DislElement self, string row, string value)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(self);
        var working = SupplyChainReading.BodyOf(model).Working().Diagram;
        return Apply(document, model, FormCommit.Row(Specification, working.ElementById(self.Id)!, row, value, Env));
    }

    /// <summary>A point on the canvas as an operation's <c>position</c> reads it: canvas units, as the definition states them.</summary>
    public static DislInvocation At(double x, double y) =>
        new(Position: new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = x, ["y"] = y });

    /// <summary>
    /// Draws the flow <paramref name="flowId"/> from the entry written as <paramref name="from"/> to the one written as
    /// <paramref name="to"/>, refused as the definition's connect constraints refuse drawing it; or its refusal.
    /// </summary>
    /// <remarks>
    /// A gesture's constraints read both ends as elements, so an end that names nothing is refused here, in the sentence
    /// of the constraint that a flow runs between nodes (supply-chain.md, "Interaction DISL cannot state").
    /// </remarks>
    public static EditOutcome Connect(LineDocument document, SupplyChainModel model, string from, string to, string flowId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = Diagram(model);
        if (ElementOf(diagram, from) is not { } source || ElementOf(diagram, to) is not { } target)
        {
            return EditOutcome.Refused("A flow runs from one node to another.");
        }

        if (GestureConstraintEvaluator.Connect(Specification, diagram, "Flow", source, target, env: Env) is { } refusal)
        {
            return EditOutcome.Refused(refusal.Message);
        }

        // The id is the host's, minted or asked for, so whether it is free is the host's to say.
        return model.IsTaken(flowId)
            ? EditOutcome.Refused("That id is already used in this diagram.")
            : Apply(document, model, new DislTransaction([new DislChange.Connect("Flow", flowId, source.Id, target.Id, new Dictionary<string, object?>(StringComparer.Ordinal))], [], null));
    }

    /// <summary>Removes the entry written as <paramref name="id"/> as the definition's deletion policy says; null when there is none.</summary>
    public static EditOutcome? Remove(LineDocument document, SupplyChainModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(Diagram(model), id) is { } element ? Apply(document, model, DeletionPolicy.Changes(Specification, element)) : null;
    }

    /// <summary>
    /// Commits <paramref name="value"/>, trimmed, to the row of the entry written as <paramref name="id"/> that writes
    /// <paramref name="key"/>; null when there is no such entry.
    /// </summary>
    public static EditOutcome? Set(LineDocument document, SupplyChainModel model, string id, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(value);
        if (ElementOf(Diagram(model), id) is not { } element)
        {
            return null;
        }

        var row = key == SupplyChainKeys.Type ? "stage" : key;
        if (!FormCommit.HasRow(Specification, element, row))
        {
            return EditOutcome.Refused($"`{key}` cannot be set on this entry.");
        }

        // The command names a group by the id it is written with; the row also takes the name a person picks, which
        // the property provider turns into the id before it sends the command.
        var text = value.Trim();
        return row == SupplyChainKeys.Group && text.Length > 0 && model.GroupOf(text) is null
            ? EditOutcome.Refused($"There is no group `{text}` in this diagram.")
            : Commit(document, model, element, row, text);
    }

    /// <summary>
    /// Steps the quantity of the node, or the volume of the flow, written as <paramref name="id"/> by
    /// <paramref name="steps"/>: the definition's increase or decrease, once per step, stopping at zero; null when there
    /// is no such entry.
    /// </summary>
    public static EditOutcome? Step(LineDocument document, SupplyChainModel model, string id, int steps)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        if (ElementOf(Diagram(model), id) is not { } element)
        {
            return null;
        }

        var what = element.IsA("Stage") ? "Quantity" : element.IsA("Flow") ? "Volume" : null;
        if (what is null)
        {
            return EditOutcome.Refused("A group has no value of its own; its members do.");
        }

        // Every step runs over the one model, which each run changes, and the steps are written as one edit: a
        // document read again between steps would need to be readable after the first.
        var operation = (steps > 0 ? "increase" : "decrease") + what;
        var working = SupplyChainReading.BodyOf(model).Working().Diagram;
        var self = ElementOf(working, id);
        var first = OperationInterpreter.Run(Specification, operation, working, self, DislIds.Fixed(), null, Env);
        if (!first.WasApplied)
        {
            return Apply(document, model, first);
        }

        List<DislChange> changes = [.. first.Changes];
        for (var step = 1; step < Math.Abs(steps); step++)
        {
            // A step past zero is the one the definition refuses; the steps before it stand.
            var next = OperationInterpreter.Run(Specification, operation, working, self, DislIds.Fixed(), null, Env);
            if (!next.WasApplied)
            {
                break;
            }

            changes.AddRange(next.Changes);
        }

        return Apply(document, model, new DislTransaction(changes, [], null));
    }

    /// <summary>Places the nodes written as the keys of <paramref name="places"/>, each at its point, in one edit; or why not.</summary>
    public static EditOutcome Place(LineDocument document, SupplyChainModel model, IReadOnlyDictionary<string, (double X, double Y)> places)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(places);
        var diagram = Diagram(model);
        List<DislChange> changes =
        [
            .. places
                .Select(place => (Node: ElementOf(diagram, place.Key), Place: place.Value))
                .Where(place => place.Node is not null && place.Node.IsA("Stage"))
                .Select(place => (DislChange)new DislChange.Set(place.Node!.Id, "Stage", Position(place.Place.X, place.Place.Y))),
        ];
        return changes.Count == 0
            ? EditOutcome.Refused("There is nothing here to place.")
            : Apply(document, model, new DislTransaction(changes, [], null));
    }

    /// <summary>
    /// The definition's <c>arrange</c>: refused as it refuses, and its <c>chain</c> layout run by the host as
    /// <see cref="SupplyChainLayout"/>, every node written where that layout draws it.
    /// </summary>
    public static EditOutcome Arrange(LineDocument document, SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var transaction = OperationInterpreter.Run(Specification, "arrange", SupplyChainReading.BodyOf(model).Working().Diagram, null, DislIds.Fixed(), env: Env);
        if (!transaction.WasApplied)
        {
            return EditOutcome.Refused(transaction.Refusal!);
        }

        var layout = transaction.HostActions.OfType<HostAction.Layout>().Single();
        var arranged = SupplyChainLayout.Of(model).Arranged;
        return arranged.Count == 0
            ? EditOutcome.Refused(layout.NothingDrawn ?? "There is nothing to arrange until this diagram has a node.")
            : Place(document, model, arranged);
    }

    /// <summary>The attributes that place an entry at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static IReadOnlyDictionary<string, object?> Position(double x, double y) =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    /// <summary>Writes <paramref name="transaction"/>, run over the DISL model of <paramref name="model"/>, into <paramref name="document"/>; or its refusal.</summary>
    public static EditOutcome Apply(LineDocument document, SupplyChainModel model, DislTransaction transaction) =>
        SupplyChainApply.Apply(document, model, Diagram(model), transaction);

    /// <summary>The DISL model of <paramref name="model"/>, as every derivation reads it. Read it, never change it.</summary>
    public static DislDiagram Diagram(SupplyChainModel model) => SupplyChainReading.BodyOf(model).Disl.Diagram;

    /// <summary>The element <paramref name="id"/> names, as <see cref="SupplyChainModel"/> finds it: the first node written with it, else the first flow, else the first group; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is null
            ? null
            : NodeOf(diagram, id)
                ?? diagram.RelationsOfType("Flow").FirstOrDefault(flow => SupplyChainWritten.IdOf(flow) == id)
                ?? diagram.NodesOfType("Group").FirstOrDefault(group => SupplyChainWritten.IdOf(group) == id);
    }

    private static DislElement? NodeOf(DislDiagram diagram, string? id) =>
        id is not null ? diagram.NodesOfType("Stage").FirstOrDefault(node => SupplyChainWritten.IdOf(node) == id) : null;
}
