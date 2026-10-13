using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// What every behavior model command handler shares: the store and the parser. The run itself -
/// load, edit a copy, save, undo with the Markdown's whole text as it was - is <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class AbmEditHandler<TCommand>(IAbmDocumentStore documents)
    : DocumentEditHandler<TCommand, IAbmDocumentStore, AbmBody, AbmModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override AbmModel Parse(AbmBody body) => body.Model;
}
