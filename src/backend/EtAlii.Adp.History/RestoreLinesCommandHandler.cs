using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.History;

/// <summary>
/// Carries out <see cref="RestoreLinesCommand{TStore}"/>. Segments were captured ascending against
/// the document as it was, so replaying them ascending rebuilds exactly that document: by the time
/// each segment is inserted, every earlier one is back and its index means what it meant.
/// </summary>
/// <remarks>
/// It reads and writes the file itself and reloads the document through the module's store, as
/// <see cref="RestoreDocumentCommandHandler{TStore}"/> does: putting lines back needs nothing of the
/// module's model, so a file that does not parse can still have its lines put back.
/// </remarks>
public sealed class RestoreLinesCommandHandler<TStore>(TStore documents, IFileWriter writer)
    : ICommandHandler<RestoreLinesCommand<TStore>>
    where TStore : IReloadableDocumentStore
{
    public async Task<CommandResult> ExecuteAsync(
        RestoreLinesCommand<TStore> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        LineDocument document;
        try
        {
            document = LineDocument.Parse(await SharedDocumentReader.ReadAllTextAsync(command.BodyPath, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the file: {exception.Message}");
        }

        foreach (var segment in command.Segments)
        {
            if (segment.Start > document.Lines.Count)
            {
                // The file has been cut down since the removal, so there is no longer a place to put
                // these back. Refusing is honest; guessing a position is not.
                return CommandResult.Failure(
                    $"{Path.GetFileName(command.BodyPath)} has changed too much since then to put that back.");
            }

            document.Insert(segment.Start, segment.Lines);
        }

        try
        {
            await writer.SaveAsync(command.BodyPath, document.Text, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the file: {exception.Message}");
        }

        // Not with the request's token: the file is written, so the store reads it whatever
        // becomes of the request.
        await documents.ReloadAsync(command.BodyPath, CancellationToken.None);
        return CommandResult.Success(command.Redo);
    }
}
