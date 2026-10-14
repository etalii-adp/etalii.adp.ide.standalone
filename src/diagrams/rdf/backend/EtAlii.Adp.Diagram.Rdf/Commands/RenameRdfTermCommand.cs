using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Renames a term everywhere it occurs, in one operation, so no reference is stranded (Requirement 5.4).</summary>
public sealed record RenameRdfTermCommand(string BodyPath, string OldIri, string NewIri) : IDocumentEditCommand;

/// <inheritdoc cref="RenameRdfTermCommand" />
public sealed class RenameRdfTermCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<RenameRdfTermCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, RenameRdfTermCommand command)
    {
        return Outcome(
            RdfWriter.RenameTerm(body, model, command.OldIri, command.NewIri));
    }
}
