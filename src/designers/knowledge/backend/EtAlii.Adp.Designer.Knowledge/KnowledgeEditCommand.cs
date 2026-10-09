using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One edit of a knowledge file: the changes a gesture came to, made to the file as one step.
/// Plain data, so the history can run it again to redo it.
/// </summary>
/// <param name="BodyPath">The knowledge file.</param>
/// <param name="Changes">What to change, in order. All of them are written, or none.</param>
internal sealed record KnowledgeEditCommand(string BodyPath, IReadOnlyList<ModelChange> Changes) : ICommand;

/// <summary>
/// Where the module's commands tell the open tables that a file was written or restored, so they
/// read it again at once rather than when the watcher gets round to it.
/// </summary>
internal interface IKnowledgeDocumentStore : IReloadableDocumentStore
{
    /// <summary>Raised with the path of a file that was just written through ADP.</summary>
    event Action<string>? Reloaded;
}

internal sealed class KnowledgeDocuments : IKnowledgeDocumentStore
{
    public event Action<string>? Reloaded;

    public void Reload(string path) => Reloaded?.Invoke(path);
}

/// <summary>
/// Carries out a <see cref="KnowledgeEditCommand"/>: reads the file as it is now, makes the changes
/// through the FBL runtime with the binding the file's format selects, and saves the result with
/// the central writer. Its inverse is the file's whole content from before, so an undo gives the
/// bytes back exactly.
/// </summary>
/// <remarks>
/// <b>All or nothing.</b> The changes are made to a copy of the open body; the first one the
/// runtime refuses ends the edit with its reason and the file is not written. So a file never
/// holds half a step - a deleted property whose cells stayed, a view without its columns.
/// </remarks>
internal sealed class KnowledgeEditCommandHandler(IKnowledgeDocumentStore documents) : ICommandHandler<KnowledgeEditCommand>
{
    public Task<CommandResult> ExecuteAsync(KnowledgeEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var name = Path.GetFileName(command.BodyPath);
        var read = KnowledgeDocumentStore.Read(command.BodyPath);
        if (read.Body is not { } body)
        {
            return Task.FromResult(CommandResult.Failure($"{name} could not be changed. {read.Refusal}"));
        }

        if (body.ReadOnlyReason.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(body.ReadOnlyReason));
        }

        (byte[]? after, string refusal) = body.Change(command.Changes);
        if (after is null)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        if (after.AsSpan().SequenceEqual(body.Bytes))
        {
            // Nothing to write and nothing to undo.
            return Task.FromResult(CommandResult.Success());
        }

        try
        {
            AdpFileWriter.Save(command.BodyPath, after);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"{name} could not be written: {exception.Message}"));
        }

        documents.Reload(command.BodyPath);

        // The restore keeps the file as text. A byte-order mark is a character of that text, so it
        // comes back with the rest; what the restore is checked against is given only for a file
        // without one, since the shared reader that does the checking leaves a mark out.
        var hasMark = after.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        return Task.FromResult(CommandResult.Success(new RestoreDocumentCommand<IKnowledgeDocumentStore>(
            command.BodyPath,
            Encoding.UTF8.GetString(body.Bytes),
            command,
            hasMark ? null : Encoding.UTF8.GetString(after))));
    }
}
