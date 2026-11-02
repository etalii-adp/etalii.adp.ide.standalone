using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Serilog;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Moves one element to a position in the document's own axis order (Requirement 7.2).
/// </summary>
/// <remarks>
/// A drag is a <b>document edit</b>, not a view change. Position is meaning on a Wardley map -
/// moving a component right asserts that it is more evolved - so this goes through the
/// project's history like every other edit, is written back to the `.owm`, and reaches every
/// other connection as ordinary deltas.
/// </remarks>
/// <param name="BodyPath">The `.owm` file to edit.</param>
/// <param name="ElementId">The element being moved.</param>
/// <param name="Visibility">Where it lands on the value-chain axis.</param>
/// <param name="Maturity">Where it lands on the evolution axis.</param>
public sealed record MoveWardleyElementCommand(
    string BodyPath,
    string ElementId,
    double Visibility,
    double Maturity) : IDocumentEditCommand;

/// <summary>
/// Applies a move by setting the coordinates that change, as values of the element the library writes in place.
/// </summary>
/// <remarks>
/// Only a coordinate whose value changes is set, so the other keeps the bytes it was written with (Requirement 5.2), and
/// the undo is the shared restore of the whole text, which gives back <c>[0.90, 0.10]</c> as it was written rather than as
/// <c>[0.9, 0.1]</c>.
/// </remarks>
public sealed class MoveWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<MoveWardleyElementCommand>(documents)
{
    private static readonly ILogger _logger = Log.ForContext<MoveWardleyElementCommandHandler>();

    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, MoveWardleyElementCommand command)
    {
        var clamped = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        var entry = map.Identities.FirstOrDefault(candidate => candidate.Id == command.ElementId);
        var changed = WardleyEdit.Attributes();

        // The holder at the entry's ordinal, where two elements carry one key (finding B9).
        if (entry is { Kind: WardleyIdentityKind.PipelineChild } && WardleyHolders.Child(map.Map, entry.Key) is { } found)
        {
            // A pipeline child has no visibility of its own - the format gives it nowhere to put one - so only its
            // maturity moves (Requirement 7.4).
            if (found.Child.Maturity != clamped.Maturity) changed["maturity"] = clamped.Maturity;
        }
        else if (map.ComponentOf(command.ElementId) is { } component)
        {
            if (component.Position.Visibility != clamped.Visibility) changed["visibility"] = clamped.Visibility;
            if (component.Position.Maturity != clamped.Maturity) changed["maturity"] = clamped.Maturity;
            _logger.Debug("Moving {Name} to {Visibility},{Maturity}", component.Name, clamped.Visibility, clamped.Maturity);
        }
        else
        {
            return EditOutcome.Refused(Gone);
        }

        if (changed.Count == 0)
        {
            return EditOutcome.Unchanged;
        }

        return body.Change(new ModelChange.Set(command.ElementId, changed)).WasApplied
            ? EditOutcome.Applied
            : EditOutcome.Refused("That element could not be moved.");
    }
}
