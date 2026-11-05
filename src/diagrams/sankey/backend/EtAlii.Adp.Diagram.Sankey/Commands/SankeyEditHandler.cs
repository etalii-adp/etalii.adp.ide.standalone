using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// What every Sankey command handler shares: the store, the body's reading, and the refusal of an entry
/// that is not there. The run itself - load, edit a copy, save, undo - is
/// <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class SankeyEditHandler<TCommand>(ISankeyDocumentStore documents)
    : DocumentEditHandler<TCommand, ISankeyDocumentStore, SankeyBody, SankeyModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal for an entry that is not there, in one wording.</summary>
    protected static EditOutcome Gone => SankeyEdits.Gone;

    /// <inheritdoc />
    protected sealed override SankeyModel Parse(SankeyBody body) => body.Reading.Model;
}
