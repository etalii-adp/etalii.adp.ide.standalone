

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// This reading's edit commands (shacl-diagram Requirement 5.1). Each is one writer operation
/// under the family's <see cref="RdfEdits.Run"/> discipline, so every one is a single undo whose
/// inverse restores the document byte for byte - including the removals that sweep a blank-node
/// subtree, which is exactly why the inverse is a whole-document snapshot rather than an
/// inverted operation.
/// </summary>
/// <remarks>
/// The commands take the shape's IRI rather than an element id: every gesture here is anchored to
/// an IRI-named shape, which is the only identity that survives the reparse between execution and
/// undo (the blank-node identity boundary).
/// </remarks>
public sealed record AddShaclPropertyRowCommand(
    string BodyPath,
    string ShapeIri,
    string PathIri,
    string DatatypeIri = "",
    int? MinCount = null,
    int? MaxCount = null,
    string Name = "") : ICommand;

/// <inheritdoc cref="AddShaclPropertyRowCommand" />
public sealed class AddShaclPropertyRowCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddShaclPropertyRowCommand>
{
    public Task<CommandResult> ExecuteAsync(AddShaclPropertyRowCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new ShaclPropertyShapeOptions(
            command.DatatypeIri.Length > 0 ? command.DatatypeIri : null,
            command.MinCount,
            command.MaxCount,
            command.Name.Length > 0 ? command.Name : null);

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.AppendPropertyShapeBlock(entry.Document, entry.Model, command.ShapeIri, command.PathIri, options));
    }
}
