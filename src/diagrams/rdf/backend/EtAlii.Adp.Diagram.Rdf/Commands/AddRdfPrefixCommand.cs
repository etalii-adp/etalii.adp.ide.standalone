using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Declares one more prefix beside the existing run (Requirement 5.7's approved half - never invented, always deliberate).</summary>
public sealed record AddRdfPrefixCommand(string BodyPath, string Prefix, string Iri) : ICommand;

/// <inheritdoc cref="AddRdfPrefixCommand" />
public sealed class AddRdfPrefixCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddRdfPrefixCommand>
{
    public Task<CommandResult> ExecuteAsync(AddRdfPrefixCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfWriter.AddPrefix(entry.Document, entry.Model, command.Prefix, command.Iri));
    }
}
