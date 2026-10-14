using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Switches a shape off or back on (Requirement 5.5).</summary>
public sealed record SetShaclDeactivatedCommand(string BodyPath, string ShapeIri, bool Deactivated) : IDocumentEditCommand;

/// <inheritdoc cref="SetShaclDeactivatedCommand" />
public sealed class SetShaclDeactivatedCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<SetShaclDeactivatedCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, SetShaclDeactivatedCommand command)
    {
        return Outcome(
            ShaclWriter.SetDeactivated(body, model, command.ShapeIri, command.Deactivated));
    }
}
