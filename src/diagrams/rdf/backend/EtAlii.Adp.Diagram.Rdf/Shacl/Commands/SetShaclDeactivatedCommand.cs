using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>Switches a shape off or back on (Requirement 5.5).</summary>
public sealed record SetShaclDeactivatedCommand(string BodyPath, string ShapeIri, bool Deactivated) : ICommand;

/// <inheritdoc cref="SetShaclDeactivatedCommand" />
public sealed class SetShaclDeactivatedCommandHandler(IRdfDocumentStore documents) : ICommandHandler<SetShaclDeactivatedCommand>
{
    public Task<CommandResult> ExecuteAsync(SetShaclDeactivatedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.SetDeactivated(entry.Document, entry.Model, command.ShapeIri, command.Deactivated));
    }
}
