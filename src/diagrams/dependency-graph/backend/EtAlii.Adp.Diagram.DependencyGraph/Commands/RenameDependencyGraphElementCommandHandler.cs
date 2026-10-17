using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a rename, as the binding sets a value. The undo is the host's restore.</summary>
public sealed class RenameDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RenameDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, RenameDependencyGraphElementCommand command) =>
        DependencyGraphEdits.Set(
            body,
            DependencyGraphEdits.Node,
            command.ElementId,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = command.Label },
            "That node is no longer in this graph.");
}
