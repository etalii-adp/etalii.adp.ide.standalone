using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>Adds a node of <paramref name="Kind"/> under <paramref name="ParentId"/>, or as a root when it is empty.</summary>
/// <param name="BodyPath">The Markdown.</param>
/// <param name="Kind">One of <see cref="AbmNodeKinds"/>' ids.</param>
/// <param name="ParentId">The parent's id; empty for a root.</param>
/// <param name="Index">Before the child now at this index; negative or past the end appends.</param>
/// <param name="Label">The label; empty takes the kind's starting label.</param>
public sealed record AddAbmNodeCommand(string BodyPath, string Kind, string ParentId, int Index, string Label = "") : ICommand;

/// <summary>Removes a node with its notes and its whole subtree.</summary>
public sealed record RemoveAbmNodeCommand(string BodyPath, string NodeId) : ICommand;

/// <summary>Sets a node's label.</summary>
public sealed record RenameAbmNodeCommand(string BodyPath, string NodeId, string Label) : ICommand;

/// <summary>Changes a node's kind; <paramref name="RetryCount"/> is a Retry's attempts and ignored otherwise.</summary>
public sealed record SetAbmNodeKindCommand(string BodyPath, string NodeId, string Kind, int RetryCount = 0) : ICommand;

/// <summary>Replaces a node's notes; empty removes them.</summary>
public sealed record SetAbmNotesCommand(string BodyPath, string NodeId, string Notes) : ICommand;

/// <summary>Moves a node and its subtree under <paramref name="NewParentId"/> (empty for the roots), before the child now at <paramref name="Index"/>.</summary>
public sealed record MoveAbmNodeCommand(string BodyPath, string NodeId, string NewParentId, int Index) : ICommand;

/// <summary>Adds a node.</summary>
public sealed class AddAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<AddAbmNodeCommand>
{
    /// <summary>The label a new node of <paramref name="kind"/> starts with, before the author types their own.</summary>
    public static string StartingLabel(string kind) => kind switch
    {
        AbmNodeKinds.Sequence => "New steps",
        AbmNodeKinds.Fallback => "New alternatives",
        AbmNodeKinds.Parallel => "New independent work",
        AbmNodeKinds.Repeat or AbmNodeKinds.Guard => "New condition",
        AbmNodeKinds.Check => "New question",
        AbmNodeKinds.Action => "New action",
        AbmNodeKinds.Ask => "New question for the user",
        AbmNodeKinds.Delegate => "New task",
        _ => "",
    };

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            AbmNode? parent = null;
            if (command.ParentId.Length > 0 && (parent = model.NodeOf(command.ParentId)) is null)
            {
                return AbmEdits.Gone();
            }

            var label = command.Label.Length > 0 ? command.Label : StartingLabel(command.Kind);
            return AbmWriter.Add(document, model, parent, command.Index, command.Kind, label).Edit;
        });
    }
}

/// <summary>Removes a node and its subtree.</summary>
public sealed class RemoveAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<RemoveAbmNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node ? AbmWriter.Remove(document, node) : AbmEdits.Gone());
    }
}

/// <summary>Sets a node's label.</summary>
public sealed class RenameAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<RenameAbmNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node ? AbmWriter.SetLabel(document, node, command.Label) : AbmEdits.Gone());
    }
}

/// <summary>Changes a node's kind.</summary>
public sealed class SetAbmNodeKindCommandHandler(IAbmDocumentStore documents) : ICommandHandler<SetAbmNodeKindCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetAbmNodeKindCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node
                ? AbmWriter.SetKind(document, node, command.Kind, command.Kind == AbmNodeKinds.Retry && command.RetryCount < 1 ? Math.Max(node.RetryCount, AbmNodeKinds.DefaultRetryCount) : command.RetryCount)
                : AbmEdits.Gone());
    }
}

/// <summary>Replaces a node's notes.</summary>
public sealed class SetAbmNotesCommandHandler(IAbmDocumentStore documents) : ICommandHandler<SetAbmNotesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetAbmNotesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node ? AbmWriter.SetNotes(document, node, command.Notes) : AbmEdits.Gone());
    }
}

/// <summary>Moves a node and its subtree.</summary>
public sealed class MoveAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<MoveAbmNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(MoveAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (model.NodeOf(command.NodeId) is not { } node)
            {
                return AbmEdits.Gone();
            }

            AbmNode? parent = null;
            if (command.NewParentId.Length > 0 && (parent = model.NodeOf(command.NewParentId)) is null)
            {
                return AbmEdit.Refused("The node it was moved under is no longer in this behavior model.");
            }

            return AbmWriter.Move(document, model, node, parent, command.Index);
        });
    }
}
