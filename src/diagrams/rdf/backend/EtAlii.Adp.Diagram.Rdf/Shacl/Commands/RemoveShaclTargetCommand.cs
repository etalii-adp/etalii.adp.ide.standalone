using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Withdraws one target declaration, addressed as its chip carries it (Requirement 5.3).</summary>
public sealed record RemoveShaclTargetCommand(string BodyPath, string ShapeIri, string TargetPredicateIri, string TermIri) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveShaclTargetCommand" />
public sealed class RemoveShaclTargetCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<RemoveShaclTargetCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, RemoveShaclTargetCommand command)
    {
        return Outcome(
            ShaclWriter.RemoveTarget(body, model, command.ShapeIri, command.TargetPredicateIri, command.TermIri));
    }
}
