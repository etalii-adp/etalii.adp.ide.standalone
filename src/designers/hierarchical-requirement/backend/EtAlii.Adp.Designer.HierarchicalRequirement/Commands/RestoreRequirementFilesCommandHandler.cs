using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Carries out a <see cref="RestoreRequirementFilesCommand"/>: every file back to what it held, or none. A file another
/// program wrote since the step is not what the step left, so nothing is written and the history is told the record is
/// spent, with its own message (FR-025, spec 029 R10, R12).
/// </summary>
internal sealed class RestoreRequirementFilesCommandHandler(IHierarchicalRequirementDocumentStore documents, IFileWriter writer) : ICommandHandler<RestoreRequirementFilesCommand>
{
    public async Task<CommandResult> ExecuteAsync(RestoreRequirementFilesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in command.Files)
        {
            if (!Holds(file.Path, file.After))
            {
                // The history's own sentence for a record another program made stale, as RestoreDocumentCommandHandler words it.
                return CommandResult.Outdated(
                    $"{Path.GetFileName(file.Path)} was changed by another program, so the edits made to it before that can no longer be undone.",
                    file.Path);
            }
        }

        List<RequirementFileState> restored = [];
        foreach (var file in command.Files)
        {
            documents.WillWrite(file.Path, file.Before);
            try
            {
                await writer.SaveAsync(file.Path, file.Before, cancellationToken);
                restored.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // What was restored before this one is written forward again: a step is every file or none.
                // Not cancelled: a put-back stopped halfway leaves half a step, which is what it exists to prevent.
                foreach (var done in restored)
                {
                    documents.WillWrite(done.Path, done.After);
                    try
                    {
                        await writer.SaveAsync(done.Path, done.After, CancellationToken.None);
                    }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                    {
                        // The failure that is reported is the one that started this.
                    }
                }

                return CommandResult.Failure($"Could not restore {Path.GetFileName(file.Path)}: {exception.Message}");
            }
        }

        foreach (var file in restored)
        {
            // Not with the request's token: the file is written, so the pages read it whatever becomes of the request.
            await documents.ReloadAsync(file.Path, CancellationToken.None);
        }

        return CommandResult.Success(command.Redo);
    }

    /// <summary>Whether <paramref name="path"/> holds exactly <paramref name="bytes"/>; a file that is gone or cannot be read does not.</summary>
    private static bool Holds(string path, byte[] bytes)
    {
        try
        {
            return File.Exists(path) && SharedDocumentReader.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
