using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>States a causal link between two declared variables.</summary>
/// <remarks>
/// The module's command for Add link and a drawn link before both ran the definition's <c>addLink</c> and
/// <c>connectLink</c> (causal-loop-disl-fbl task 11), kept as the oracle <see cref="CausalLoopDerivedOperationsTests"/>
/// holds them to.
/// </remarks>
public sealed record AddLinkCommand(string BodyPath, string From, string To, CausalLoopPolarity Polarity) : IDocumentEditCommand;

/// <inheritdoc cref="AddLinkCommand" />
public sealed class AddLinkCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<AddLinkCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, AddLinkCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!model.Declares(command.From) || !model.Declares(command.To)) return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
        if (CausalLoopEdits.HasLink(model, command.From, command.To)) return EditOutcome.Refused(CausalLoopEdits.LinkTaken);

        // The definition's hook claims every feedback loop the new link closes, in the same undoable
        // edit: a link and the loops it creates are the one gesture the user made.
        return Draw(body, command.From, command.To, CausalLoopEdits.Stored(command.Polarity));
    }

    /// <summary>
    /// Draws the link from the variable <paramref name="from"/> to <paramref name="to"/>, stating
    /// <paramref name="polarity"/> (none for null), and writes it with every loop the definition's
    /// <c>on: connect</c> hook claims for it, as one edit (causal-loop-disl-fbl Requirement 5.5). Both
    /// variables are declared and the link is not yet stated; the caller checked.
    /// </summary>
    private static EditOutcome Draw(CausalLoopBody body, string from, string to, string? polarity)
    {
        var diagram = body.Working();
        var cause = CausalLoopDefinition.VariableOf(diagram, from) ?? throw new ArgumentException($"No variable is named '{from}'.", nameof(from));
        var effect = CausalLoopDefinition.VariableOf(diagram, to) ?? throw new ArgumentException($"No variable is named '{to}'.", nameof(to));

        // The cause and effect names are the binding's reading of the ends, which the hook's cycles are
        // keyed by; only the polarity is written.
        var written = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (polarity is not null) written["polarity"] = polarity;
        var read = new Dictionary<string, object?>(written, StringComparer.Ordinal) { ["causeName"] = from, ["effectName"] = to };
        var id = CausalLoopEdits.LinkId(from, to);
        var link = diagram.AddRelation("CausalLink", id, cause, effect, read);

        var claimed = HookRunner.AfterConnect(CausalLoopDefinition.Specification, link, new ClaimedLoopIds(), CausalLoopDefinition.EditEnv);
        return claimed.WasApplied
            ? CausalLoopDefinition.Apply(body, new DislTransaction([new DislChange.Connect("CausalLink", id, cause.Id, effect.Id, written), .. claimed.Changes], [], null))
            : EditOutcome.Refused(claimed.Refusal!);
    }

    /// <summary>The ids of the loops a hook claims while it runs: unique on the working diagram, and never written.</summary>
    private sealed class ClaimedLoopIds : IIdSource
    {
        private int _next;

        public string Next(string type) => string.Create(CultureInfo.InvariantCulture, $"{type}:claimed-{++_next}");
    }
}
