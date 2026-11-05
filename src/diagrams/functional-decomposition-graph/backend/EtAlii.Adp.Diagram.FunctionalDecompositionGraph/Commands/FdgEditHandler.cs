using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// What every functional decomposition graph command handler shares: the store, the model of the body an
/// edit writes, and the refusal of an element that is not there. The run itself - load, edit a copy, save,
/// undo - is <see cref="DocumentEditRun"/>'s, and a body that could not be read is refused there before any
/// command sees it.
/// </summary>
/// <remarks>
/// <b>Every edit is a change of the DISL model written through the binding</b>: a handler builds a
/// <see cref="DislTransaction"/> and <see cref="FdgDefinition.Apply"/> writes it into the copy of the body as
/// splices (<see cref="FdgBody.Change"/>). No handler builds a line of YAML.
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
}
