using EtAlii.Adp.Common;
namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>Creates one folder. The inverse removes it again - only while it is still empty.</summary>
/// <param name="FullPath">Absolute path of the folder to create; resolved and validated by the caller.</param>
public sealed record CreateFolderCommand(string FullPath) : ICommand;

/// <summary>
/// Undoes a <see cref="CreateFolderCommand"/>. Deliberately not a general delete: it refuses a
/// folder that has gained content since, because an undo must never take work with it.
/// </summary>
public sealed record RemoveCreatedFolderCommand(string FullPath) : ICommand;

public sealed class CreateFolderCommandHandler : ICommandHandler<CreateFolderCommand>
{
    public Task<CommandResult> ExecuteAsync(CreateFolderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(command.FullPath) || File.Exists(command.FullPath))
        {
            return Task.FromResult(CommandResult.Failure("Something with that name is already there."));
        }

        try
        {
            Directory.CreateDirectory(command.FullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not create the folder: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(new RemoveCreatedFolderCommand(command.FullPath)));
    }
}

public sealed class RemoveCreatedFolderCommandHandler : ICommandHandler<RemoveCreatedFolderCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveCreatedFolderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(command.FullPath))
        {
            return Task.FromResult(CommandResult.Failure("That folder is no longer there."));
        }

        if (Directory.EnumerateFileSystemEntries(command.FullPath).Any())
        {
            // The folder gained content after it was created; removing it now would take that
            // work with it, which an undo must never do.
            return Task.FromResult(CommandResult.Failure("The folder is not empty any more, so undoing its creation would delete its content."));
        }

        try
        {
            Directory.Delete(command.FullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not remove the folder: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(new CreateFolderCommand(command.FullPath)));
    }
}
