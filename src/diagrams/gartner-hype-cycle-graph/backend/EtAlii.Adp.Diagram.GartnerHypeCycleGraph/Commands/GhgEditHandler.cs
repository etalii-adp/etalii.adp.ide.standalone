using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// What every hype cycle graph command handler shares: the store and the parser. The run itself -
/// load, edit a copy, save, undo - is <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class GhgEditHandler<TCommand>(IGhgDocumentStore documents)
    : DocumentEditHandler<TCommand, IGhgDocumentStore, GhgBody, GhgModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override GhgModel Parse(GhgBody body) => GhgParser.Parse(body);
}
