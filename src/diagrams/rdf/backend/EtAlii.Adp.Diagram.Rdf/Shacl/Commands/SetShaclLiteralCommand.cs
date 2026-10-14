using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Sets a shape's <c>sh:name</c> or <c>sh:description</c> from the property grid - the one edit
/// Requirement 6.3 routes through the grid rather than through a menu, because it is a plain
/// literal on an IRI-named subject and has nowhere else to live.
/// </summary>
public sealed record SetShaclLiteralCommand(string BodyPath, string ShapeIri, string PredicateIri, string Value) : IDocumentEditCommand;

/// <inheritdoc cref="SetShaclLiteralCommand" />
public sealed class SetShaclLiteralCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<SetShaclLiteralCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, SetShaclLiteralCommand command)
    {
        return Outcome(
            ShaclWriter.SetLiteral(body, model, command.ShapeIri, command.PredicateIri, command.Value));
    }
}
