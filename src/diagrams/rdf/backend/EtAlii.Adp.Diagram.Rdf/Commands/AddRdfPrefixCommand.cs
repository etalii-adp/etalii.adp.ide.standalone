using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Declares one more prefix beside the existing run (Requirement 5.7's approved half - never invented, always deliberate).</summary>
public sealed record AddRdfPrefixCommand(string BodyPath, string Prefix, string Iri) : IDocumentEditCommand;

/// <inheritdoc cref="AddRdfPrefixCommand" />
public sealed class AddRdfPrefixCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<AddRdfPrefixCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, AddRdfPrefixCommand command)
    {
        return Outcome(
            RdfWriter.AddPrefix(body, model, command.Prefix, command.Iri));
    }
}
