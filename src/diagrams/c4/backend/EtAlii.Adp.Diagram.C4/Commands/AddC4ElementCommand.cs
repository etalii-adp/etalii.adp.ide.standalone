

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.C4;

/// <summary>Adds one element to a C4 model, inside <paramref name="ParentId"/> or at the top when it is empty.</summary>
/// <param name="ParentId">
/// The element the new one goes inside - a software system for a container, a container for a
/// component. Empty puts it at the top of the model, which is where a person and a software
/// system belong.
/// </param>
/// <param name="Identifier">
/// The DSL identifier to declare it under. Chosen by the handler when empty, which is the
/// normal case: an identifier is ADP's business rather than the user's.
/// </param>
public sealed record AddC4ElementCommand(
    string BodyPath,
    C4ElementKind Kind,
    string Name,
    string ParentId = "",
    string Identifier = "") : ICommand;

/// <summary>Undoes an <see cref="AddC4ElementCommand"/>: removes the element it declared.</summary>
/// <param name="CollapseParent">
/// The element whose block the add had to open, when it did - so undoing the add takes the
/// braces away again rather than leaving an empty block behind. Empty when the parent already
/// had a block, or when there was no parent at all.
/// </param>
public sealed record RemoveC4ElementCommand(string BodyPath, string Identifier, string CollapseParent = "") : ICommand;

internal sealed class AddC4ElementCommandHandler(IC4DocumentStore documents) : ICommandHandler<AddC4ElementCommand>
{
    public Task<CommandResult> ExecuteAsync(AddC4ElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = documents.WorkspaceOf(command.BodyPath);
        var name = command.Name.Trim();
        if (name.Length == 0)
        {
            return Task.FromResult(CommandResult.Failure("An element needs a name."));
        }

        if (C4Placement.Refuse(workspace, command.Kind, command.ParentId) is { } refusal)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var identifier = command.Identifier.Length > 0
            ? command.Identifier
            : C4Placement.IdentifierFor(workspace, name);
        if (workspace.Find(identifier) is not null)
        {
            return Task.FromResult(CommandResult.Failure($"This model already has something called '{identifier}'."));
        }

        var document = documents.GetOrLoad(command.BodyPath);
        if (!C4Placement.TryInsert(document, workspace, command.Kind, name, identifier, command.ParentId, out var error, out var openedBlock))
        {
            return Task.FromResult(CommandResult.Failure(error));
        }

        var writeError = documents.Save(command.BodyPath);
        return Task.FromResult(writeError.Length == 0
            ? CommandResult.Success(
                new RemoveC4ElementCommand(command.BodyPath, identifier, openedBlock ? command.ParentId : ""))
            : CommandResult.Failure(writeError));
    }
}

internal sealed class RemoveC4ElementCommandHandler(IC4DocumentStore documents) : ICommandHandler<RemoveC4ElementCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveC4ElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = documents.WorkspaceOf(command.BodyPath);
        if (workspace.Find(command.Identifier) is not { } element)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this model."));
        }

        var document = documents.GetOrLoad(command.BodyPath);
        var inverse = new AddC4ElementCommand(
            command.BodyPath,
            element.Kind,
            element.Name,
            element.ParentId ?? "",
            element.Id);

        // Resolved before the removal shifts anything: the parent whose block the add had to
        // open, if it opened one.
        var parent = command.CollapseParent.Length > 0 ? workspace.Find(command.CollapseParent) : null;

        // Only ever undoing an add, so the element is a single line with no block of its own -
        // anything with children was not added this way and is not removed this way either.
        if (!C4Placement.TryRemove(document, element, out var error))
        {
            return Task.FromResult(CommandResult.Failure(error));
        }

        if (parent is not null)
        {
            // The braces the add opened come off again. Without this, undoing an add would
            // leave `container "Web" … { }` behind - not what was there before, which is the
            // one thing undo has to deliver.
            C4Placement.TryCollapseEmptyBlock(document, parent);
        }

        var writeError = documents.Save(command.BodyPath);
        return Task.FromResult(writeError.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(writeError));
    }
}
