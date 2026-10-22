using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// What every functional decomposition graph command handler shares: the store, the reading of the body
/// an edit writes, and the refusal of an element that is not there. The run itself - load, edit a copy,
/// save, undo - is <see cref="DocumentEditRun"/>'s, and a body that could not be read is refused there
/// before any command sees it.
/// </summary>
/// <remarks>
/// <b>Writing is still <see cref="FdgWriter"/>'s</b> until every edit is written by splices
/// (functional-decomposition-graph-disl-fbl task 10). The copy of the body is read through the binding
/// like every other; each command then writes the copy's text with <see cref="FdgWriter"/>, at the lines
/// <see cref="FdgParser"/> finds, and the copy reads what was written (<see cref="FdgBody.Rewrite"/>).
/// </remarks>
public abstract class FdgEditHandler<TCommand>(IFdgDocumentStore documents)
    : DocumentEditHandler<TCommand, IFdgDocumentStore, FdgBody, DislModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal for an element that is not there, in one wording.</summary>
    protected static EditOutcome Gone => EditOutcome.Refused("That element is no longer in this graph.");

    /// <inheritdoc />
    protected sealed override DislModel Parse(FdgBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Disl;
    }

    /// <inheritdoc />
    protected sealed override EditOutcome Apply(FdgBody body, DislModel model, TCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        var document = LineDocument.Parse(body.Text);
        var outcome = Apply(document, FdgParser.Parse(document), command);
        if (outcome.WasApplied && !outcome.IsUnchanged) body.Rewrite(document.Text);
        return outcome;
    }

    /// <summary>Writes <paramref name="command"/> to <paramref name="document"/>, whose lines <paramref name="model"/> found, or says why not.</summary>
    protected abstract EditOutcome Apply(LineDocument document, FdgModel model, TCommand command);
}
