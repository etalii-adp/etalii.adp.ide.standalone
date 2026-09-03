using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// States one more triple (Requirement 5.1). The object is an IRI where
/// <paramref name="ObjectIri"/> is set, and a literal built from the remaining fields otherwise -
/// empty strings meaning absent.
/// </summary>
/// <param name="BodyPath">The RDF file.</param>
/// <param name="SubjectIri">The subject's full IRI.</param>
/// <param name="PredicateIri">The predicate's full IRI.</param>
/// <param name="ObjectIri">The object's full IRI, or empty for a literal object.</param>
/// <param name="ObjectLiteral">The literal object's lexical form.</param>
/// <param name="ObjectLanguage">The literal's language tag, without its <c>@</c>.</param>
/// <param name="ObjectDatatypeIri">The literal's datatype IRI.</param>
public sealed record AddRdfTripleCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string ObjectIri, string ObjectLiteral = "", string ObjectLanguage = "", string ObjectDatatypeIri = "") : ICommand;

/// <summary>Removes one triple, matched by its three values (Requirement 5.2).</summary>
public sealed record RemoveRdfTripleCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string ObjectIri, string ObjectLiteral = "", string ObjectLanguage = "", string ObjectDatatypeIri = "") : ICommand;

/// <summary>Removes a resource and every triple it is subject or object of, as one undo (Requirement 5.3).</summary>
public sealed record RemoveRdfResourceCommand(string BodyPath, string Iri) : ICommand;

/// <summary>Renames a term everywhere it occurs, in one operation, so no reference is stranded (Requirement 5.4).</summary>
public sealed record RenameRdfTermCommand(string BodyPath, string OldIri, string NewIri) : ICommand;

/// <summary>
/// Rewrites one literal object in place - what a label or documentation edit is. The triple is
/// matched by subject, predicate and its current literal value; the new value replaces exactly
/// that token.
/// </summary>
public sealed record ReplaceRdfObjectLiteralCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string OldLexical, string OldLanguage, string OldDatatypeIri,
    string NewLexical, string NewLanguage = "", string NewDatatypeIri = "") : ICommand;

/// <summary>Declares one more prefix beside the existing run (Requirement 5.7's approved half - never invented, always deliberate).</summary>
public sealed record AddRdfPrefixCommand(string BodyPath, string Prefix, string Iri) : ICommand;

/// <inheritdoc cref="AddRdfTripleCommand" />
public sealed class AddRdfTripleCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddRdfTripleCommand>
{
    public Task<CommandResult> ExecuteAsync(AddRdfTripleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        RdfTerm objectTerm = command.ObjectIri.Length > 0
            ? new IriTerm(command.ObjectIri, "")
            : new LiteralTerm(
                command.ObjectLiteral,
                command.ObjectDatatypeIri.Length > 0 ? command.ObjectDatatypeIri : null,
                command.ObjectLanguage.Length > 0 ? command.ObjectLanguage : null,
                "");

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfWriter.AddTriple(entry.Document, entry.Model, command.SubjectIri, command.PredicateIri, objectTerm));
    }
}

/// <inheritdoc cref="RemoveRdfTripleCommand" />
public sealed class RemoveRdfTripleCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveRdfTripleCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveRdfTripleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfEdits.TripleOf(
                entry.Model, command.SubjectIri, command.PredicateIri,
                command.ObjectIri, command.ObjectLiteral, command.ObjectLanguage, command.ObjectDatatypeIri) is { } triple
                ? RdfWriter.RemoveTriple(entry.Document, entry.Model, triple)
                : "That triple is not in the file as it stands, so there is nothing to remove.");
    }
}

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

/// <inheritdoc cref="ReplaceRdfObjectLiteralCommand" />
public sealed class ReplaceRdfObjectLiteralCommandHandler(IRdfDocumentStore documents) : ICommandHandler<ReplaceRdfObjectLiteralCommand>
{
    public Task<CommandResult> ExecuteAsync(ReplaceRdfObjectLiteralCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfEdits.TripleOf(
                entry.Model, command.SubjectIri, command.PredicateIri,
                "", command.OldLexical, command.OldLanguage, command.OldDatatypeIri) is { } triple
                ? RdfWriter.ReplaceObjectLiteral(
                    entry.Document, entry.Model, triple,
                    command.NewLexical,
                    command.NewLanguage.Length > 0 ? command.NewLanguage : null,
                    command.NewDatatypeIri.Length > 0 ? command.NewDatatypeIri : null)
                : "That value is not in the file as it stands, so there is nothing to rewrite.");
    }
}

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
