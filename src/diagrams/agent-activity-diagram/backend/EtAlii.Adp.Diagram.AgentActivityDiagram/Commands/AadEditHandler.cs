using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The handler of every command that edits an activity file: saved against the file as it is now,
/// where an agent wrote it since it was read (Requirement 8.6), with what every edit shares around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A newer file is shown and not changed</b>: one written for a later version of the activity
/// file is refused before the command's own edit runs.
/// </para>
/// <para>
/// <b>What the reader set for an element an agent has since removed goes with the write</b>, without
/// a word: it describes nothing any more (Requirement 8.8).
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class AadEditHandler<TCommand>(IAadDocumentStore documents)
    : CurrentDocumentEditHandler<TCommand, IAadDocumentStore, AadBody, AadModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override AadModel Parse(AadBody body) => AadModel.Read(body);

    /// <inheritdoc />
    protected sealed override EditOutcome Apply(AadBody body, AadModel model, TCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(command);

        if (body.Version > AadBody.KnownVersion)
        {
            return EditOutcome.Refused($"{Path.GetFileName(command.BodyPath)} was written for version {body.Version} of the activity file, which this version of ADP shows and does not change.");
        }

        var outcome = Edit(body, command);
        if (!outcome.WasApplied)
        {
            return outcome;
        }

        foreach (var orphan in body.OrphanedViewEntries)
        {
            var removed = body.Change(new ModelChange.Remove(orphan));
            if (!removed.WasApplied) return removed;
        }

        return outcome;
    }

    /// <summary>Changes <paramref name="document"/> as <paramref name="command"/> asks, or says why not.</summary>
    protected abstract EditOutcome Edit(AadBody document, TCommand command);
}
