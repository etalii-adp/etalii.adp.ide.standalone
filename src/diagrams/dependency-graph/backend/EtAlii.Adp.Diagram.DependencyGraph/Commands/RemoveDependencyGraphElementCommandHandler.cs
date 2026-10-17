using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a removal: the node, and with it every dependency whose end resolves to it, as the binding's
/// cascade takes them (a dependency naming the node's id that resolves to an earlier holder stays). One undo
/// step, the host's restore, puts them all back.
/// </summary>
public sealed class RemoveDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RemoveDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, RemoveDependencyGraphElementCommand command) =>
        DependencyGraphEdits.Remove(body, DependencyGraphEdits.Node, command.ElementId, "That node is no longer in this graph.");
}
