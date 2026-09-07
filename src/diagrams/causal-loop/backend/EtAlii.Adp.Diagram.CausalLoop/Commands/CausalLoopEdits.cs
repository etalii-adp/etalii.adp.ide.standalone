using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The discipline every edit command in this module shares: check the document reads, capture its
/// bytes, run one writer operation - which refuses before any splice - save through the store, and
/// report a byte-exact restore as the inverse.
/// </summary>
/// <remarks>
/// The inverse is a whole-document snapshot rather than a reversing edit. Some operations here
/// touch several distant lines at once - renaming a variable moves every link and loop that names
/// it - the documents are small, and a snapshot cannot be wrong about what it is putting back. The
/// redo of that inverse is the original command, so redoing re-runs the same edit.
/// </remarks>
internal static class CausalLoopEdits
{
    /// <summary>Runs one writer edit as a command body; see the class remarks.</summary>
    public static Task<CommandResult> Run(
        ICausalLoopDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<CausalLoopDocumentEntry, string> edit)
    {
        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This causal loop diagram could not be read, so nothing can be edited until it is fixed."));
        }

        var before = entry.Document.Text;

        var refusal = edit(entry);
        if (refusal.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var error = documents.Save(bodyPath);
        if (error.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(error));
        }

        return Task.FromResult(CommandResult.Success(new RestoreCausalLoopDocumentCommand(bodyPath, before, self)));
    }
}

/// <summary>
/// Puts a document back byte for byte - the inverse every edit here reports, so undoing one
/// restores comments, blank lines and spacing exactly as the author had them.
/// </summary>
/// <param name="BodyPath">The document to restore.</param>
/// <param name="Text">Its complete text as captured before the edit.</param>
/// <param name="Redo">The original command, so redoing the undo runs the same edit again.</param>
public sealed record RestoreCausalLoopDocumentCommand(string BodyPath, string Text, ICommand Redo) : ICommand;

/// <inheritdoc cref="RestoreCausalLoopDocumentCommand" />
public sealed class RestoreCausalLoopDocumentCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<RestoreCausalLoopDocumentCommand>
{
    public Task<CommandResult> ExecuteAsync(
        RestoreCausalLoopDocumentCommand command, CancellationToken cancellationToken = default)
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

        documents.Reload(command.BodyPath);
        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}
