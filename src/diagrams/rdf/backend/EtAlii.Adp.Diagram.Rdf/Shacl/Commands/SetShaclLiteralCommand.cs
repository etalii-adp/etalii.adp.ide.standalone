

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Sets a shape's <c>sh:name</c> or <c>sh:description</c> from the property grid - the one edit
/// Requirement 6.3 routes through the grid rather than through a menu, because it is a plain
/// literal on an IRI-named subject and has nowhere else to live.
/// </summary>
public sealed record SetShaclLiteralCommand(string BodyPath, string ShapeIri, string PredicateIri, string Value) : ICommand;

/// <inheritdoc cref="SetShaclLiteralCommand" />
public sealed class SetShaclLiteralCommandHandler(IRdfDocumentStore documents) : ICommandHandler<SetShaclLiteralCommand>
{
    public Task<CommandResult> ExecuteAsync(SetShaclLiteralCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.SetLiteral(entry.Document, entry.Model, command.ShapeIri, command.PredicateIri, command.Value));
    }
}
