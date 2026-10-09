using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>Drops a toolbox tool: a new element with the tool's initial values, locked where it was dropped (Requirement 6.11).</summary>
public sealed record AddAadElementCommand(string BodyPath, string Tool, double X, double Y) : ICommand;

/// <summary>Adds a task to a specification or a pull request to a location, updated now.</summary>
public sealed record AddAadRowCommand(string BodyPath, string ParentId) : ICommand;

/// <summary>Removes an element or a row, with what the definition's deletion policy takes or clears with it.</summary>
public sealed record RemoveAadElementCommand(string BodyPath, string ElementId) : ICommand;

/// <summary>Sets one attribute of an element or a row, as the property grid and a rename do. A row's change is stamped.</summary>
public sealed record SetAadAttributeCommand(string BodyPath, string ElementId, string Attribute, string Value) : ICommand;

/// <summary>Sets a task's status through the definition's own operation, which stamps the task.</summary>
public sealed record SetAadTaskStatusCommand(string BodyPath, string TaskId, string Status) : ICommand;

/// <summary>Draws the one relation the two elements' types allow, in whichever order they were given.</summary>
public sealed record ConnectAadCommand(string BodyPath, string FromId, string ToId) : ICommand;

/// <summary>Removes a relation: the key that states it is cleared, and both elements stay.</summary>
public sealed record DisconnectAadCommand(string BodyPath, string RelationId) : ICommand;

/// <summary>Locks an element at a position, or moves the lock it has.</summary>
public sealed record PinAadElementCommand(string BodyPath, string ElementId, double X, double Y) : ICommand;

/// <summary>Unlocks one element, or with no element every one.</summary>
public sealed record UnpinAadElementCommand(string BodyPath, string ElementId = "") : ICommand;

/// <summary>Folds or unfolds one group of one element. Only a state that differs from the group's default is kept.</summary>
public sealed record SetAadGroupCommand(string BodyPath, string ElementId, string Group, bool Collapsed) : ICommand;

/// <summary>Shows or hides archived specifications.</summary>
public sealed record SetAadShowArchivedCommand(string BodyPath, bool Show) : ICommand;

public sealed class AddAadElementCommandHandler(IAadDocumentStore documents) : ICommandHandler<AddAadElementCommand>
{
    public Task<CommandResult> ExecuteAsync(AddAadElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            var position = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = command.X, ["y"] = command.Y };
            var transaction = OperationInterpreter.Drop(AadDefinition.Specification, command.Tool, document.Disl.Diagram, position, AadDefinition.NewIds, AadDefinition.Env());
            var added = AadDefinition.Apply(document, transaction);
            if (!added.WasApplied || transaction.Changes.OfType<DislChange.Create>().FirstOrDefault() is not { } created)
            {
                return added;
            }

            return document.Change(AadEdits.Placement(created.Id, command.X, command.Y));
        });
    }
}

public sealed class AddAadRowCommandHandler(IAadDocumentStore documents) : ICommandHandler<AddAadRowCommand>
{
    public Task<CommandResult> ExecuteAsync(AddAadRowCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            if (AadDefinition.ElementOf(document.Disl.Diagram, command.ParentId) is not { } parent)
            {
                return AadEdits.Gone();
            }

            var now = AadEdits.Now();
            (string Type, Dictionary<string, object?> Attributes)? row = parent.IsA("Specification")
                ? ("Task", new(StringComparer.Ordinal) { ["title"] = "New task", ["status"] = "pending", ["updated"] = now })
                : parent.IsA("Location")
                    ? ("PullRequest", new(StringComparer.Ordinal) { ["title"] = "New pull request", ["updated"] = now })
                    : null;
            return row is { } add
                ? AadDefinition.Apply(document, new DislTransaction([new DislChange.Create(add.Type, AadDefinition.NewIds.Next(add.Type), add.Attributes, parent.Id, null)], [], null))
                : AadEdit.Refused("Only a specification lists tasks, and only a location lists pull requests.");
        });
    }
}

public sealed class RemoveAadElementCommandHandler(IAadDocumentStore documents) : ICommandHandler<RemoveAadElementCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveAadElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Tasks and pull requests are written inside their element, and a locked position or a
        // group state goes with the element it names by the binding's own cascade.
        return AadEdits.Run(documents, command.BodyPath, command, document =>
            AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { Type.IsRelation: false } element
                ? AadDefinition.Apply(document, DeletionPolicy.Changes(AadDefinition.Specification, element, nested: true))
                : AadEdits.Gone());
    }
}

public sealed class SetAadAttributeCommandHandler(IAadDocumentStore documents) : ICommandHandler<SetAadAttributeCommand>
{
    public Task<CommandResult> ExecuteAsync(SetAadAttributeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            if (AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is not { Type.IsRelation: false } element)
            {
                return AadEdits.Gone();
            }

            if (!element.Type.Attributes.TryGetValue(command.Attribute, out var attribute))
            {
                return AadEdit.Refused($"A {element.Type.Name} has no '{command.Attribute}'.");
            }

            // An enumeration is given by its label, its name or its stored word; a reference by the id it names.
            object? value = AadDefinition.EnumOf(attribute.Type) is { } choices
                ? choices.Members.FirstOrDefault(member => member.Label == command.Value || member.Name == command.Value || member.Stored == command.Value)?.Name
                : command.Value.Length == 0
                    ? null
                    : command.Value;
            if (value is null && AadDefinition.EnumOf(attribute.Type) is not null)
            {
                return AadEdit.Refused($"'{command.Value}' is not one of the values this can have.");
            }

            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { [command.Attribute] = DislWrite.StoredForm(AadDefinition.Specification, element.Type.Name, command.Attribute, value) };
            // A change to a task or a pull request is the moment it was last updated, unless the moment itself is what was set.
            if (command.Attribute != "updated" && element.Type.Attributes.ContainsKey("updated"))
            {
                attributes["updated"] = AadEdits.Now();
            }

            return document.Change(new ModelChange.Set(element.Id, attributes));
        });
    }
}

public sealed class SetAadTaskStatusCommandHandler(IAadDocumentStore documents) : ICommandHandler<SetAadTaskStatusCommand>
{
    public Task<CommandResult> ExecuteAsync(SetAadTaskStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            var diagram = document.Disl.Diagram;
            if (AadDefinition.ElementOf(diagram, command.TaskId) is not { } task || !task.IsA("Task"))
            {
                return AadEdits.Gone();
            }

            var invocation = new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["status"] = command.Status });
            return AadDefinition.Apply(document, OperationInterpreter.Run(AadDefinition.Specification, "setTaskStatus", diagram, task, AadDefinition.NewIds, invocation, AadDefinition.Env()));
        });
    }
}

public sealed class ConnectAadCommandHandler(IAadDocumentStore documents) : ICommandHandler<ConnectAadCommand>
{
    public Task<CommandResult> ExecuteAsync(ConnectAadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            var diagram = document.Disl.Diagram;
            if (AadDefinition.ElementOf(diagram, command.FromId) is not { } from || AadDefinition.ElementOf(diagram, command.ToId) is not { } to)
            {
                return AadEdits.Gone();
            }

            // A relation has one direction in the file; the gesture may be drawn either way.
            if (AadDefinition.RelationBetween(from, to) is not { } relation)
            {
                return AadEdit.Refused(AadDefinition.NoRelation(from, to));
            }

            if (AadDefinition.Occupied(relation) is { } occupied)
            {
                return AadEdit.Refused(occupied);
            }

            return AadDefinition.Apply(document, OperationInterpreter.Connect(
                AadDefinition.Specification, relation.Type.Type, diagram, relation.Source, relation.Target, AadDefinition.NewIds, AadDefinition.Env()));
        });
    }
}

public sealed class DisconnectAadCommandHandler(IAadDocumentStore documents) : ICommandHandler<DisconnectAadCommand>
{
    public Task<CommandResult> ExecuteAsync(DisconnectAadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
            AadDefinition.ElementOf(document.Disl.Diagram, command.RelationId) is { Type.IsRelation: true } relation && AadDefinition.Holder(relation) is { } holder
                ? document.Change(new ModelChange.Set(holder.Element.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [holder.Attribute] = null }))
                : AadEdits.Gone());
    }
}

public sealed class PinAadElementCommandHandler(IAadDocumentStore documents) : ICommandHandler<PinAadElementCommand>
{
    public Task<CommandResult> ExecuteAsync(PinAadElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
            AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { Type.IsRelation: false, Parent: null }
                ? document.Change(AadEdits.Placement(document, command.ElementId, command.X, command.Y))
                : AadEdits.Gone());
    }
}

public sealed class UnpinAadElementCommandHandler(IAadDocumentStore documents) : ICommandHandler<UnpinAadElementCommand>
{
    public Task<CommandResult> ExecuteAsync(UnpinAadElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            var pinned = document.Model.Elements
                .Where(element => element.Type == "Placement" && (command.ElementId.Length == 0 || element.Id == AadEdits.PlacementId(command.ElementId)))
                .Select(element => element.Id)
                .ToList();
            if (pinned.Count == 0)
            {
                return AadEdit.Refused(command.ElementId.Length == 0 ? "No position is locked." : "Its position is not locked.");
            }

            foreach (var id in pinned)
            {
                var edit = document.Change(new ModelChange.Remove(id));
                if (!edit.WasApplied) return edit;
            }

            return AadEdit.Applied;
        });
    }
}

public sealed class SetAadGroupCommandHandler(IAadDocumentStore documents) : ICommandHandler<SetAadGroupCommand>
{
    public Task<CommandResult> ExecuteAsync(SetAadGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
        {
            if (AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is not { } element || !AadDefinition.GroupsOf(element).Contains(command.Group))
            {
                return AadEdits.Gone();
            }

            var id = $"groups:{command.ElementId}#{command.Group}";
            var stored = document.Model.Elements.FirstOrDefault(candidate => candidate.Id == id);
            var isDefault = AadDefinition.CollapsedByDefault.Contains(command.Group) == command.Collapsed;

            // The file says only what differs from the definition's default (Requirement 5.6), so a
            // group put back as it comes loses its entry.
            if (isDefault)
            {
                return stored is null ? AadEdit.Applied : document.Change(new ModelChange.Remove(id));
            }

            return document.Change(stored is null
                ? new ModelChange.Add("GroupState", id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["element"] = command.ElementId, ["group"] = AadDefinition.StoredGroup(command.Group), ["collapsed"] = command.Collapsed })
                : new ModelChange.Set(id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["collapsed"] = command.Collapsed }));
        });
    }
}

public sealed class SetAadShowArchivedCommandHandler(IAadDocumentStore documents) : ICommandHandler<SetAadShowArchivedCommand>
{
    public Task<CommandResult> ExecuteAsync(SetAadShowArchivedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return AadEdits.Run(documents, command.BodyPath, command, document =>
            document.Change(new ModelChange.Set("diagram", new Dictionary<string, object?>(StringComparer.Ordinal) { ["showArchived"] = command.Show })));
    }
}

/// <summary>What every command shares: the run against the file as it is, and the entries of the reader's part.</summary>
internal static class AadEdits
{
    /// <summary>
    /// Runs one edit and saves it against the file as it is now: where an agent wrote the file since
    /// it was read, the edit is applied to what the agent wrote (Requirement 8.6). Its undo restores
    /// the text the edit was applied to, and refuses once the file is no longer what the edit left.
    /// </summary>
    public static Task<CommandResult> Run(IAadDocumentStore documents, string bodyPath, ICommand self, Func<AadBody, AadEdit> edit)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(edit);

        var saved = documents.Save(bodyPath, documents.GetOrLoad(bodyPath), document =>
        {
            if (document.Version > AadBody.KnownVersion)
            {
                return AadEdit.Refused($"{Path.GetFileName(bodyPath)} was written for version {document.Version} of the activity file, which this version of ADP shows and does not change.");
            }

            var outcome = edit(document);
            if (!outcome.WasApplied)
            {
                return outcome;
            }

            // What the reader set for an element an agent has since removed goes with this write,
            // without a word: it describes nothing any more (Requirement 8.8).
            foreach (var orphan in document.OrphanedViewEntries)
            {
                var removed = document.Change(new ModelChange.Remove(orphan));
                if (!removed.WasApplied) return removed;
            }

            return outcome;
        });
        if (saved.Result.Failed)
        {
            return Task.FromResult(CommandResult.Failure(saved.Result.Error));
        }

        var undo = new RestoreDocumentCommand<IAadDocumentStore>(bodyPath, saved.Before, self, saved.After);
        return Task.FromResult(saved.Result.Warning.Length > 0 ? CommandResult.Success(undo, saved.Result.Warning) : CommandResult.Success(undo));
    }

    /// <summary>The moment of a gesture as the file writes one: local time with its offset, to the second.</summary>
    public static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    public static string PlacementId(string elementId) => $"pinned:{elementId}";

    /// <summary>The lock of a new element.</summary>
    public static ModelChange Placement(string elementId, double x, double y) =>
        new ModelChange.Add("Placement", PlacementId(elementId), new Dictionary<string, object?>(StringComparer.Ordinal) { ["element"] = elementId, ["x"] = Whole(x), ["y"] = Whole(y) });

    /// <summary>The lock of an element the file has: moved where it has one, added where it has none.</summary>
    public static ModelChange Placement(AadBody document, string elementId, double x, double y) =>
        document.Model.Elements.Any(element => element.Id == PlacementId(elementId))
            ? new ModelChange.Set(PlacementId(elementId), new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = Whole(x), ["y"] = Whole(y) })
            : Placement(elementId, x, y);

    public static AadEdit Gone() => AadEdit.Refused("That is no longer in this diagram.");

    /// <summary>A position to the canvas unit: a drag gives fractions nobody reads, and the file is read by people.</summary>
    private static object Whole(double value) => (long)Math.Round(value);
}
