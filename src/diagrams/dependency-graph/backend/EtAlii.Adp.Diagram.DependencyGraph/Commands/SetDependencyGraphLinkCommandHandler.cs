using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Handles <see cref="SetDependencyGraphLinkCommand"/>.</summary>
public sealed class SetDependencyGraphLinkCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<SetDependencyGraphLinkCommand>(documents)
{
    private const string LinkedNode = "LinkedNode";

    /// <inheritdoc />
    /// <remarks>Without its link the entry is read by the ordinary node rule, whose label it then takes.</remarks>
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, SetDependencyGraphLinkCommand command) =>
        DependencyGraphEdits.Set(
            body,
            LinkedNode,
            command.ElementId,
            command.Link is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal) { ["link"] = null, ["label"] = command.Label }
                : new Dictionary<string, object?>(StringComparer.Ordinal) { ["link"] = command.Link },
            "That node is no longer in this graph.");
}
