using EtAlii.Adp.Editor;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// A text editor's save, as data the history can replay: the full new text of one file
/// (modular-text-editors Requirement 6.2 - a save is an <c>ICommand</c> on the project's
/// history, one undo away like every other change).
/// </summary>
/// <param name="FullPath">Absolute path of the file; resolved and containment-checked by the caller.</param>
/// <param name="Content">The whole text to save. The buffer re-applies the file's own terminators.</param>
public sealed record SaveTextFileCommand(string FullPath, string Content) : ICommand;

/// <summary>
/// Applies a <see cref="SaveTextFileCommand"/> through <see cref="TextFileBuffer"/>, so the
/// write keeps the file's encoding and each existing line's own terminator (Requirement 6.1)
/// and uses the family's temp-then-move discipline (Requirement 6.3). The inverse is another
/// save carrying the text the file held before - undo puts the old content back through the
/// very same preserving path.
/// </summary>
public sealed class SaveTextFileCommandHandler : ICommandHandler<SaveTextFileCommand>
{
    // No logger of its own: TextFileBuffer reports its refusals as values, which travel to
    // the user, and the history logs what it executes.

    public async Task<CommandResult> ExecuteAsync(SaveTextFileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-opened on every run rather than trusting earlier state: undo and redo dispatch
        // this again later, when the file's shape may have changed underneath.
        var opened = TextFileBuffer.Open(command.FullPath);
        if (opened.Buffer is null)
        {
            return CommandResult.Failure(opened.Refusal);
        }

        var previousContent = opened.Buffer.Content;
        var error = await opened.Buffer.SaveAsync(command.Content, cancellationToken);
        if (error.Length > 0)
        {
            return CommandResult.Failure(error);
        }

        return CommandResult.Success(new SaveTextFileCommand(command.FullPath, previousContent));
    }
}
