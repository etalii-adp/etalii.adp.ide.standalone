using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>Drops a toolbox tool: a new element with the tool's initial values, locked where it was dropped (Requirement 6.11).</summary>
/// <remarks>
/// The new element's id is the command's, minted when the gesture is made. Redo runs the command
/// again, and what was added, joined and undone must come back as the element the later edits name.
/// </remarks>
public sealed record AddAadElementCommand(string BodyPath, string Tool, double X, double Y) : IDocumentEditCommand
{
    public string NewId { get; } = AadDefinition.NewIds.Next(Tool);
}

/// <summary>Adds a task to a specification or a pull request to a location, updated now.</summary>
/// <remarks>The row's id is the command's, for the reason <see cref="AddAadElementCommand"/> gives.</remarks>
public sealed record AddAadRowCommand(string BodyPath, string ParentId) : IDocumentEditCommand
{
    public string NewId { get; } = AadDefinition.NewIds.Next("row");
}

/// <summary>Removes an element or a row, with what the definition's deletion policy takes or clears with it.</summary>
public sealed record RemoveAadElementCommand(string BodyPath, string ElementId) : IDocumentEditCommand;

/// <summary>Sets one attribute of an element or a row, as the property grid and a rename do. A row's change is stamped.</summary>
public sealed record SetAadAttributeCommand(string BodyPath, string ElementId, string Attribute, string Value) : IDocumentEditCommand;

/// <summary>Sets a task's status through the definition's own operation, which stamps the task.</summary>
public sealed record SetAadTaskStatusCommand(string BodyPath, string TaskId, string Status) : IDocumentEditCommand;

/// <summary>Draws the one relation the two elements' types allow, in whichever order they were given.</summary>
public sealed record ConnectAadCommand(string BodyPath, string FromId, string ToId) : IDocumentEditCommand;

/// <summary>Removes a relation: the key that states it is cleared, and both elements stay.</summary>
public sealed record DisconnectAadCommand(string BodyPath, string RelationId) : IDocumentEditCommand;

/// <summary>Locks an element at a position, or moves the lock it has.</summary>
public sealed record PinAadElementCommand(string BodyPath, string ElementId, double X, double Y) : IDocumentEditCommand;

/// <summary>Unlocks one element, or with no element every one.</summary>
public sealed record UnpinAadElementCommand(string BodyPath, string ElementId = "") : IDocumentEditCommand;

/// <summary>Folds or unfolds one group of one element. Only a state that differs from the group's default is kept.</summary>
public sealed record SetAadGroupCommand(string BodyPath, string ElementId, string Group, bool Collapsed) : IDocumentEditCommand;

/// <summary>Shows or hides archived specifications.</summary>
public sealed record SetAadShowArchivedCommand(string BodyPath, bool Show) : IDocumentEditCommand;

public sealed class AddAadElementCommandHandler(IAadDocumentStore documents) : AadEditHandler<AddAadElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, AddAadElementCommand command)
    {
        var position = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = command.X, ["y"] = command.Y };
        var transaction = OperationInterpreter.Drop(AadDefinition.Specification, command.Tool, document.Disl.Diagram, position, DislIds.Fixed(command.NewId), AadDefinition.Env());
        var added = AadDefinition.Apply(document, transaction);
        if (!added.WasApplied || transaction.Changes.OfType<DislChange.Create>().FirstOrDefault() is not { } created)
        {
            return added;
        }

        return document.Change(AadEdits.Placement(created.Id, command.X, command.Y));
    }
}

public sealed class AddAadRowCommandHandler(IAadDocumentStore documents) : AadEditHandler<AddAadRowCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, AddAadRowCommand command)
    {
        if (AadDefinition.ElementOf(document.Disl.Diagram, command.ParentId) is not { } parent)
        {
            return AadEdits.Gone();
        }

        var now = AadEdits.Now();
        (string Type, Dictionary<string, object?> Attributes)? row = parent.IsA("Specification")
            ? ("Task", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "New task", ["status"] = "pending", ["updated"] = now })
            : parent.IsA("Location")
                ? ("PullRequest", new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "New pull request", ["updated"] = now })
                : null;
        return row is { } add
            ? AadDefinition.Apply(document, new DislTransaction([new DislChange.Create(add.Type, command.NewId, add.Attributes, parent.Id, null)], [], null))
            : EditOutcome.Refused("Only a specification lists tasks, and only a location lists pull requests.");
    }
}

public sealed class RemoveAadElementCommandHandler(IAadDocumentStore documents) : AadEditHandler<RemoveAadElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, RemoveAadElementCommand command)
    {
        // Tasks and pull requests are written inside their element, and a locked position or a
        // group state goes with the element it names by the binding's own cascade.
        return AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { Type.IsRelation: false } element
            ? AadDefinition.Apply(document, DeletionPolicy.Changes(AadDefinition.Specification, element, nested: true))
            : AadEdits.Gone();
    }
}

public sealed class SetAadAttributeCommandHandler(IAadDocumentStore documents) : AadEditHandler<SetAadAttributeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, SetAadAttributeCommand command)
    {
        if (AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is not { Type.IsRelation: false } element)
        {
            return AadEdits.Gone();
        }

        if (!element.Type.Attributes.TryGetValue(command.Attribute, out var attribute))
        {
            return EditOutcome.Refused($"A {element.Type.Name} has no '{command.Attribute}'.");
        }

        // An enumeration is given by its label, its name or its stored word; a reference by the id it names.
        object? value = AadDefinition.EnumOf(attribute.Type) is { } choices
            ? choices.Members.FirstOrDefault(member => member.Label == command.Value || member.Name == command.Value || member.Stored == command.Value)?.Name
            : command.Value.Length == 0
                ? null
                : command.Value;
        if (value is null && AadDefinition.EnumOf(attribute.Type) is not null)
        {
            return EditOutcome.Refused($"'{command.Value}' is not one of the values this can have.");
        }

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { [command.Attribute] = DislWrite.StoredForm(AadDefinition.Specification, element.Type.Name, command.Attribute, value) };
        // A change to a task or a pull request is the moment it was last updated, unless the moment itself is what was set.
        if (command.Attribute != "updated" && element.Type.Attributes.ContainsKey("updated"))
        {
            attributes["updated"] = AadEdits.Now();
        }

        return document.Change(new ModelChange.Set(element.Id, attributes));
    }
}

public sealed class SetAadTaskStatusCommandHandler(IAadDocumentStore documents) : AadEditHandler<SetAadTaskStatusCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, SetAadTaskStatusCommand command)
    {
        var diagram = document.Disl.Diagram;
        if (AadDefinition.ElementOf(diagram, command.TaskId) is not { } task || !task.IsA("Task"))
        {
            return AadEdits.Gone();
        }

        var invocation = new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["status"] = command.Status });
        return AadDefinition.Apply(document, OperationInterpreter.Run(AadDefinition.Specification, "setTaskStatus", diagram, task, AadDefinition.NewIds, invocation, AadDefinition.Env()));
    }
}

public sealed class ConnectAadCommandHandler(IAadDocumentStore documents) : AadEditHandler<ConnectAadCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, ConnectAadCommand command)
    {
        var diagram = document.Disl.Diagram;
        if (AadDefinition.ElementOf(diagram, command.FromId) is not { } from || AadDefinition.ElementOf(diagram, command.ToId) is not { } to)
        {
            return AadEdits.Gone();
        }

        // A relation has one direction in the file; the gesture may be drawn either way.
        if (AadDefinition.RelationBetween(from, to) is not { } relation)
        {
            return EditOutcome.Refused(AadDefinition.NoRelation(from, to));
        }

        if (AadDefinition.Occupied(relation) is { } occupied)
        {
            return EditOutcome.Refused(occupied);
        }

        return AadDefinition.Apply(document, OperationInterpreter.Connect(
            AadDefinition.Specification, relation.Type.Type, diagram, relation.Source, relation.Target, AadDefinition.NewIds, AadDefinition.Env()));
    }
}

public sealed class DisconnectAadCommandHandler(IAadDocumentStore documents) : AadEditHandler<DisconnectAadCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, DisconnectAadCommand command) =>
        AadDefinition.ElementOf(document.Disl.Diagram, command.RelationId) is { Type.IsRelation: true } relation && AadDefinition.Holder(relation) is { } holder
            ? document.Change(new ModelChange.Set(holder.Element.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [holder.Attribute] = null }))
            : AadEdits.Gone();
}

public sealed class PinAadElementCommandHandler(IAadDocumentStore documents) : AadEditHandler<PinAadElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, PinAadElementCommand command) =>
        AadDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { Type.IsRelation: false, Parent: null }
            ? document.Change(AadEdits.Placement(document, command.ElementId, command.X, command.Y))
            : AadEdits.Gone();
}

public sealed class UnpinAadElementCommandHandler(IAadDocumentStore documents) : AadEditHandler<UnpinAadElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, UnpinAadElementCommand command)
    {
        var pinned = document.Model.Elements
            .Where(element => element.Type == "Placement" && (command.ElementId.Length == 0 || element.Id == AadEdits.PlacementId(command.ElementId)))
            .Select(element => element.Id)
            .ToList();
        if (pinned.Count == 0)
        {
            return EditOutcome.Refused(command.ElementId.Length == 0 ? "No position is locked." : "Its position is not locked.");
        }

        foreach (var id in pinned)
        {
            var edit = document.Change(new ModelChange.Remove(id));
            if (!edit.WasApplied) return edit;
        }

        return EditOutcome.Applied;
    }
}

public sealed class SetAadGroupCommandHandler(IAadDocumentStore documents) : AadEditHandler<SetAadGroupCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, SetAadGroupCommand command)
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
            return stored is null ? EditOutcome.Applied : document.Change(new ModelChange.Remove(id));
        }

        return document.Change(stored is null
            ? new ModelChange.Add("GroupState", id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["element"] = command.ElementId, ["group"] = AadDefinition.StoredGroup(command.Group), ["collapsed"] = command.Collapsed })
            : new ModelChange.Set(id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["collapsed"] = command.Collapsed }));
    }
}

public sealed class SetAadShowArchivedCommandHandler(IAadDocumentStore documents) : AadEditHandler<SetAadShowArchivedCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Edit(AadBody document, SetAadShowArchivedCommand command) =>
        document.Change(new ModelChange.Set("diagram", new Dictionary<string, object?>(StringComparer.Ordinal) { ["showArchived"] = command.Show }));
}

/// <summary>What every command shares: the entries of the reader's part, and the refusal of an element that is gone.</summary>
internal static class AadEdits
{
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

    public static EditOutcome Gone() => EditOutcome.Refused("That is no longer in this diagram.");

    /// <summary>A position to the canvas unit: a drag gives fractions nobody reads, and the file is read by people.</summary>
    private static object Whole(double value) => (long)Math.Round(value);
}
