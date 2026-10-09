using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// Carries out <see cref="RestoreDocumentCommand{TStore}"/>: saves the text, reloads the document
/// through the module's store, and reports a failure to write as a failed command (R6.1).
/// </summary>
/// <remarks>
/// The reload goes through the store rather than being left to the file watcher, so every open
/// session hears about the restored state at once. A failure to write stops before the reload, so
/// a document nobody could write is not re-read as though it had changed.
/// </remarks>
public sealed class RestoreDocumentCommandHandler<TStore>(TStore documents)
    : ICommandHandler<RestoreDocumentCommand<TStore>>
    where TStore : IReloadableDocumentStore
{
    public Task<CommandResult> ExecuteAsync(
        RestoreDocumentCommand<TStore> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.After is not null && !Holds(command.BodyPath, command.After))
        {
            // Another program wrote the file after the edit this would undo. Restoring the text from
            // before that edit would take the other program's change with it, so nothing is written
            // and the history is told this record is spent.
            return Task.FromResult(CommandResult.Outdated(
                $"{Path.GetFileName(command.BodyPath)} was changed by another program, so the edits made to it before that can no longer be undone.",
                command.BodyPath));
        }

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

    /// <summary>
    /// Whether <paramref name="path"/> holds exactly <paramref name="text"/>. A file that is gone or
    /// cannot be read does not: its state is not the one the edit left, whatever else it is.
    /// </summary>
    private static bool Holds(string path, string text)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return string.Equals(SharedDocumentReader.ReadAllText(path), text, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
