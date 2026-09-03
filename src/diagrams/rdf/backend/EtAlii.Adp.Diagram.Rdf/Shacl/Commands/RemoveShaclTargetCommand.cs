using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Withdraws one target declaration, addressed as its chip carries it (Requirement 5.3).</summary>
public sealed record RemoveShaclTargetCommand(string BodyPath, string ShapeIri, string TargetPredicateIri, string TermIri) : ICommand;

/// <inheritdoc cref="RemoveShaclTargetCommand" />
public sealed class RemoveShaclTargetCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveShaclTargetCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveShaclTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.RemoveTarget(entry.Document, entry.Model, command.ShapeIri, command.TargetPredicateIri, command.TermIri));
    }
}
