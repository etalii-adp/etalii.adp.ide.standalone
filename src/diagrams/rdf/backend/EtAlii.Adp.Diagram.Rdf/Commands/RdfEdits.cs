using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The discipline every module edit command shares: check the document parses, capture its
/// bytes, run one writer operation (which refuses before any splice), save through the store -
/// and report a byte-exact restore as the inverse.
/// </summary>
/// <remarks>
/// The inverse is a whole-document snapshot, deliberately: this family's operations touch several
/// distant ranges at once (rename-with-references, remove-with-edges), the documents are small,
/// and a snapshot cannot be wrong. The redo of that inverse is the original command instance, so
/// a redo re-runs the same edit against the restored document.
/// </remarks>
internal static class RdfEdits
{
    /// <summary>Runs one writer edit as a command body; see the class remarks.</summary>
    public static Task<CommandResult> Run(
        IRdfDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<RdfDocumentEntry, string> edit)
    {
        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This file does not parse, so nothing can be edited until it is fixed."));
        }

        var before = entry.Document.Text;
        var refusal = edit(entry);
        if (refusal.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var error = documents.Save(bodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RestoreRdfDocumentCommand(bodyPath, before, self))
            : CommandResult.Failure(error));
    }

    /// <summary>
    /// The triple a command addresses, matched by its three values: an IRI object where
    /// <paramref name="objectIri"/> is set, and a literal matched on lexical form, language and
    /// datatype otherwise - empty strings meaning absent, as commands carry them.
    /// </summary>
    public static RdfTriple? TripleOf(
        RdfModel model, string subjectIri, string predicateIri,
        string objectIri, string lexical, string language, string datatypeIri) =>
        model.Triples.FirstOrDefault(t =>
            t.Subject is IriTerm subject
            && subject.Iri == subjectIri
            && t.Predicate.Iri == predicateIri
            && (objectIri.Length > 0
                ? t.Object is IriTerm o && o.Iri == objectIri
                : t.Object is LiteralTerm l
                    && l.Lexical == lexical
                    && (l.Language ?? "") == language
                    && (l.DatatypeIri ?? "") == datatypeIri));
}

/// <summary>
/// Puts a document back byte for byte - the inverse every module edit reports, so undoing one
/// restores comments, formatting and abbreviation style exactly as they were.
/// </summary>
/// <param name="BodyPath">The document to restore.</param>
/// <param name="Text">Its complete text as captured before the edit.</param>
/// <param name="Redo">The original command, so redoing the undo runs the same edit again.</param>
public sealed record RestoreRdfDocumentCommand(string BodyPath, string Text, ICommand Redo) : ICommand;

/// <inheritdoc cref="RestoreRdfDocumentCommand" />
public sealed class RestoreRdfDocumentCommandHandler(IRdfDocumentStore documents)
    : ICommandHandler<RestoreRdfDocumentCommand>
{
    public Task<CommandResult> ExecuteAsync(RestoreRdfDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            AdpFileWriter.Save(command.BodyPath, command.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not restore the file: {exception.Message}"));
        }

        // Through the store's reload, so every open session hears about the restored state.
        documents.Reload(command.BodyPath);
        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}
