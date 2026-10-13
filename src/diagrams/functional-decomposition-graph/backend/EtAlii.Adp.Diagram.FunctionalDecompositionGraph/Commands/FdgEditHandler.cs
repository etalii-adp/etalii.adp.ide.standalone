using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// What every functional decomposition graph command handler shares: the store, the parser, and
/// the refusal of an element that is not there. The run itself - load, edit a copy, save, undo -
/// is <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class FdgEditHandler<TCommand>(IFdgDocumentStore documents)
    : DocumentEditHandler<TCommand, IFdgDocumentStore, LineDocument, FdgModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal for an element that is not there, in one wording.</summary>
    protected static EditOutcome Gone => EditOutcome.Refused("That element is no longer in this graph.");

    /// <inheritdoc />
    protected sealed override FdgModel Parse(LineDocument body) => FdgParser.Parse(body);
}
