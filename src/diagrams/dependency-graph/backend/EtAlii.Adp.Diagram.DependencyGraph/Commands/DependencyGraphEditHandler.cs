using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// A handler of a command that edits a dependency graph: the shared run, which edits a copy, saves
/// it and carries a warning of the save to the result (code-structure FR-033), with the model a
/// dependency graph's parser reads.
/// </summary>
/// <remarks>
/// Every dependency graph edit names its own undo through <see cref="EditOutcome.UndoneBy"/> - the
/// same command with the old values, its opposite, or a restore of the removed lines - so an undo
/// is located against the document as it is then rather than putting a whole earlier text back.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class DependencyGraphEditHandler<TCommand>(IDependencyGraphDocumentStore documents)
    : DocumentEditHandler<TCommand, IDependencyGraphDocumentStore, LineDocument, DependencyGraphModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override DependencyGraphModel Parse(LineDocument body) => DependencyGraphParser.Parse(body);
}
