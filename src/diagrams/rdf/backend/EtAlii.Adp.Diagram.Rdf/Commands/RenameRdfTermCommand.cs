using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Renames a term everywhere it occurs, in one operation, so no reference is stranded (Requirement 5.4).</summary>
public sealed record RenameRdfTermCommand(string BodyPath, string OldIri, string NewIri) : ICommand;

/// <inheritdoc cref="RenameRdfTermCommand" />
public sealed class RenameRdfTermCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RenameRdfTermCommand>
{
    public Task<CommandResult> ExecuteAsync(RenameRdfTermCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfWriter.RenameTerm(entry.Document, entry.Model, command.OldIri, command.NewIri));
    }
}
