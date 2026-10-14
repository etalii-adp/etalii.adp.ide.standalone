using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Bows a link's arc to the other side of its chord, or puts it back.</summary>
public sealed record SetLinkCurvatureCommand(string BodyPath, string From, string To, bool Flipped) : IDocumentEditCommand;

/// <inheritdoc cref="SetLinkCurvatureCommand" />
public sealed class SetLinkCurvatureCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLinkCurvatureCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLinkCurvatureCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LinkId(command.From, command.To), "CausalLink", ("flipped", command.Flipped))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
