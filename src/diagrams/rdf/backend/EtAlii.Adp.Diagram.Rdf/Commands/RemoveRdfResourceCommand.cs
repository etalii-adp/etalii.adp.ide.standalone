

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Removes a resource and every triple it is subject or object of, as one undo (Requirement 5.3).</summary>
public sealed record RemoveRdfResourceCommand(string BodyPath, string Iri) : ICommand;

/// <inheritdoc cref="RemoveRdfResourceCommand" />
public sealed class RemoveRdfResourceCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveRdfResourceCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveRdfResourceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfWriter.RemoveResource(entry.Document, entry.Model, command.Iri));
    }
}
