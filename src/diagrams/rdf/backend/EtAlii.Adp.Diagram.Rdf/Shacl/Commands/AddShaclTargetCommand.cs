using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Declares one more target on a shape (Requirement 5.3).</summary>
public sealed record AddShaclTargetCommand(string BodyPath, string ShapeIri, string TargetPredicateIri, string TermIri) : ICommand;

/// <inheritdoc cref="AddShaclTargetCommand" />
public sealed class AddShaclTargetCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddShaclTargetCommand>
{
    public Task<CommandResult> ExecuteAsync(AddShaclTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.AddTarget(entry.Document, entry.Model, command.ShapeIri, command.TargetPredicateIri, new IriTerm(command.TermIri, "")));
    }
}
