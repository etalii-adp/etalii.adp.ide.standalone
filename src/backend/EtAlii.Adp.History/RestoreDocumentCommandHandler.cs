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
