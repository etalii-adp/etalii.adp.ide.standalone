using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

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

/// <summary>A parent line drawn from <paramref name="ParentId"/> to <paramref name="ChildId"/>: the child moves, with its subtree, to be the parent's last child.</summary>
public sealed record ConnectAbmChildCommand(string BodyPath, string ParentId, string ChildId) : ICommand;

/// <summary>Adds a node.</summary>
/// <remarks>
/// The definition's <c>addChild</c> adds a node as a parent's last child; any other add is its
/// <c>addHere</c>, placed where the command says - under the nearest node above a drop, the host's
/// placement <c>underNearestAbove</c> (<c>create.place</c>) - and given the command's label when it has one.
/// </remarks>
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

            var diagram = document.Disl.Diagram;
            var invocation = new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = AbmEdits.TypeOf(command.Kind) });
            if (parent is not null && command is { Index: < 0, Label.Length: 0 })
            {
                return AbmDefinition.Apply(document, OperationInterpreter.Run(
                    AbmDefinition.Specification, "addChild", diagram, AbmDefinition.ElementOf(diagram, parent.Id), AbmDefinition.NewIds, invocation, AbmDefinition.Env));
            }

            return AbmDefinition.Apply(
                document,
                OperationInterpreter.Run(AbmDefinition.Specification, "addHere", diagram, null, AbmDefinition.NewIds, invocation, AbmDefinition.Env),
                change => change is ModelChange.Add add
                    ? add with
                    {
                        ParentId = parent?.Id,
                        Index = command.Index,
                        Attributes = command.Label.Length > 0 ? new Dictionary<string, object?>(add.Attributes, StringComparer.Ordinal) { ["label"] = command.Label } : add.Attributes,
                    }
                    : change);
        });
    }
}

/// <summary>Removes a node and its subtree: the definition's deletion, the subtree going with the node's own lines.</summary>
public sealed class RemoveAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<RemoveAbmNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node
                ? AbmDefinition.Apply(document, DeletionPolicy.Changes(AbmDefinition.Specification, AbmDefinition.ElementOf(document.Disl.Diagram, node.Id)!, nested: true))
                : AbmEdits.Gone());
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
            model.NodeOf(command.NodeId) is { } node ? document.Change(AbmEdits.Set(node, "label", command.Label)) : AbmEdits.Gone());
    }
}

/// <summary>Changes a node's kind.</summary>
/// <remarks>
/// A change to another kind is the definition's <c>behavior.retype</c>, a Retry's attempts taken from its
/// <c>attributeMapping</c>. Setting a Retry's attempts, and a kind to itself, rewrite the keyword in place
/// as they always have.
/// </remarks>
public sealed class SetAbmNodeKindCommandHandler(IAbmDocumentStore documents) : ICommandHandler<SetAbmNodeKindCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetAbmNodeKindCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is not { } node
                ? AbmEdits.Gone()
                : node.Kind != command.Kind && command.RetryCount < 1 && AbmNodeKinds.IsKnown(command.Kind)
                ? AbmDefinition.Apply(document, RetypePolicy.Change(
                    AbmDefinition.Specification, AbmDefinition.ElementOf(document.Disl.Diagram, node.Id)!, AbmEdits.TypeOf(command.Kind), AbmDefinition.Env))
                : document.Change(new ModelChange.Retype(node.Id, AbmEdits.TypeOf(command.Kind), new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["attempts"] = command is { Kind: AbmNodeKinds.Retry, RetryCount: < 1 } ? Math.Max(node.RetryCount, AbmNodeKinds.DefaultRetryCount) : command.RetryCount,
                })));
    }
}

/// <summary>Replaces a node's notes: the definition's <c>editNotes</c>.</summary>
public sealed class SetAbmNotesCommandHandler(IAbmDocumentStore documents) : ICommandHandler<SetAbmNotesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetAbmNotesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, model) =>
            model.NodeOf(command.NodeId) is { } node
                ? AbmDefinition.Apply(document, OperationInterpreter.Run(
                    AbmDefinition.Specification,
                    "editNotes",
                    document.Disl.Diagram,
                    AbmDefinition.ElementOf(document.Disl.Diagram, node.Id),
                    AbmDefinition.NewIds,
                    new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["notes"] = command.Notes }),
                    AbmDefinition.Env))
                : AbmEdits.Gone());
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

            return document.Change(new ModelChange.Move(node.Id, parent?.Id, command.Index));
        });
    }
}

/// <summary>
/// Runs a drawn parent line: the derived <c>Child</c> relation's <c>connect</c> edit, the definition's
/// <c>moveUnder</c>, whose aborts refuse a line that cannot move the child in the writer's words.
/// </summary>
public sealed class ConnectAbmChildCommandHandler(IAbmDocumentStore documents) : ICommandHandler<ConnectAbmChildCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectAbmChildCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AbmEdits.Run(documents, command.BodyPath, command, (document, _) =>
        {
            var diagram = document.Disl.Diagram;
            if (AbmDefinition.ElementOf(diagram, command.ChildId) is not { } child)
            {
                return AbmEdits.Gone();
            }

            return AbmDefinition.ElementOf(diagram, command.ParentId) is not { } parent ? AbmEdit.Refused("The node it was moved under is no longer in this behavior model.") : AbmDefinition.Apply(document, OperationInterpreter.Connect(AbmDefinition.Specification, "Child", diagram, parent, child, AbmDefinition.NewIds, AbmDefinition.Env));
        });
    }
}
