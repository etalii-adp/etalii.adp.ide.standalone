using EtAlii.Adp.Backend;

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

/// <summary>States a new node shape (Requirement 5.2).</summary>
public sealed record CreateShaclNodeShapeCommand(string BodyPath, string ShapeIri) : ICommand;

/// <inheritdoc cref="CreateShaclNodeShapeCommand" />
public sealed class CreateShaclNodeShapeCommandHandler(IRdfDocumentStore documents) : ICommandHandler<CreateShaclNodeShapeCommand>
{
    public Task<CommandResult> ExecuteAsync(CreateShaclNodeShapeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.CreateNodeShape(entry.Document, entry.Model, command.ShapeIri));
    }
}

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

/// <summary>
/// Removes a shape and every blank-node subtree reachable only from it (Requirement 5.6). The
/// count the confirmation states comes from <see cref="ShaclWriter.CountShapeRemoval"/>, read
/// before the command is dispatched.
/// </summary>
public sealed record RemoveShaclShapeCommand(string BodyPath, string ShapeIri) : ICommand;

/// <inheritdoc cref="RemoveShaclShapeCommand" />
public sealed class RemoveShaclShapeCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveShaclShapeCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveShaclShapeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.RemoveShapeWithSubtrees(entry.Document, entry.Model, command.ShapeIri));
    }
}
