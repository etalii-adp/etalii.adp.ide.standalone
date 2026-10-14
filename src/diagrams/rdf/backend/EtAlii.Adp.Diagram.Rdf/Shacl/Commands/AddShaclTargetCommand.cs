using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Declares one more target on a shape (Requirement 5.3).</summary>
public sealed record AddShaclTargetCommand(string BodyPath, string ShapeIri, string TargetPredicateIri, string TermIri) : IDocumentEditCommand;

/// <inheritdoc cref="AddShaclTargetCommand" />
public sealed class AddShaclTargetCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<AddShaclTargetCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, AddShaclTargetCommand command)
    {
        return Outcome(
            ShaclWriter.AddTarget(body, model, command.ShapeIri, command.TargetPredicateIri, new IriTerm(command.TermIri, "")));
    }
}
