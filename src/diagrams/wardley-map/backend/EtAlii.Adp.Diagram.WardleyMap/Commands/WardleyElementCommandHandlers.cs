using System.Globalization;
using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The handlers for the element commands. They share one shape: locate against <b>current</b>
/// state, refuse with a sentence when that fails, edit through <see cref="WardleyWriter"/>, and
/// report an inverse.
/// </summary>
/// <remarks>
/// Preconditions are always checked against the document as it is now rather than trusted from
/// when the command was made, because undo and redo dispatch the same instance again later
/// (Requirement 9.5).
/// </remarks>
internal static class WardleyEdit
{
    /// <summary>The component an element id names, or null.</summary>
    public static WardleyComponent? ComponentOf(
        WardleyMap map,
        IReadOnlyList<WardleyIdentityEntry> identities,
        string elementId)
    {
        var entry = identities.FirstOrDefault(candidate =>
            candidate.Id == elementId && candidate.Kind == WardleyIdentityKind.Component);

        return entry is null
            ? null
            : map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
    }

    /// <summary>A number as the DSL writes it, without a trailing zero.</summary>
    public static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed class AddWardleyElementCommandHandler : ICommandHandler<AddWardleyElementCommand>
{
    private static readonly string[] Kinds = ["component", "anchor", "submap"];

    private readonly IWardleyDocumentStore _documents;

    public AddWardleyElementCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(AddWardleyElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Kinds.Contains(command.Kind, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(CommandResult.Failure($"A Wardley map has no '{command.Kind}' statement."));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Task.FromResult(CommandResult.Failure("An element needs a name."));
        }

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);

        // Two components sharing a name makes every reference to it ambiguous, which
        // Requirement 14.4 reports as an error - so refusing to create one is kinder than
        // writing it and reporting it a moment later.
        if (map.Components.Any(existing => existing.Name == command.Name))
        {
            return Task.FromResult(CommandResult.Failure($"This map already has an element called '{command.Name}'."));
        }

        var before = document.ToText();
        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        WardleyWriter.Append(
            document,
            $"{command.Kind.ToLowerInvariant()} {command.Name} [{WardleyEdit.Number(position.Visibility)}, {WardleyEdit.Number(position.Maturity)}]");

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before)));
    }
}

public sealed class RemoveWardleyElementCommandHandler : ICommandHandler<RemoveWardleyElementCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public RemoveWardleyElementCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(RemoveWardleyElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var component = WardleyEdit.ComponentOf(map, _documents.Identities(command.BodyPath), command.ElementId);
        if (component is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        var before = document.ToText();

        // Everything that names it goes with it, or the document is left referring to something
        // that does not exist. Descending, so removing one line cannot shift the next.
        var lines = new List<uint> { component.Line };
        lines.AddRange(map.Links
            .Where(link => link.Source == component.Name || link.Target == component.Name)
            .Select(link => link.Line));
        lines.AddRange(map.Evolves.Where(evolve => evolve.Name == component.Name).Select(evolve => evolve.Line));

        foreach (var line in lines.Distinct().OrderByDescending(line => line))
        {
            WardleyWriter.RemoveLine(document, line);
        }

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before)));
    }
}

public sealed class RenameWardleyElementCommandHandler : ICommandHandler<RenameWardleyElementCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public RenameWardleyElementCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(RenameWardleyElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.NewName))
        {
            return Task.FromResult(CommandResult.Failure("An element needs a name."));
        }

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var component = WardleyEdit.ComponentOf(map, _documents.Identities(command.BodyPath), command.ElementId);
        if (component is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        if (map.Components.Any(existing => existing.Name == command.NewName))
        {
            return Task.FromResult(CommandResult.Failure($"This map already has an element called '{command.NewName}'."));
        }

        var before = document.ToText();

        // One pass over the declaration and every reference, because Requirement 9.6's undo only
        // restores them together if they moved together.
        if (WardleyWriter.Rename(document, map, component.Name, command.NewName) == 0)
        {
            return Task.FromResult(CommandResult.Failure("That element could not be renamed."));
        }

        // The identity travels with the name, in this same command. Reconciliation matches by
        // key and a component's key IS its name, so without this a rename would look like one
        // element vanishing and another arriving - breaking the selection and every undo entry
        // that names it (Requirement 4.4).
        _documents.Rekey(command.BodyPath, WardleyIdentityKind.Component, component.Name, command.NewName);

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before)));
    }
}

public sealed class SetWardleyInertiaCommandHandler : ICommandHandler<SetWardleyInertiaCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyInertiaCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(SetWardleyInertiaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var component = WardleyEdit.ComponentOf(map, _documents.Identities(command.BodyPath), command.ElementId);
        if (component is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        var before = document.Lines[(int)component.Line - 1];
        if (!WardleyWriter.SetInertia(document, component, command.Inertia))
        {
            return Task.FromResult(CommandResult.Failure("That element's inertia could not be changed."));
        }

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(
            new RestoreWardleyLineCommand(command.BodyPath, component.Line, before)));
    }
}

public sealed class SetWardleyDecoratorCommandHandler : ICommandHandler<SetWardleyDecoratorCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyDecoratorCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(SetWardleyDecoratorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var component = WardleyEdit.ComponentOf(map, _documents.Identities(command.BodyPath), command.ElementId);
        if (component is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        var before = document.Lines[(int)component.Line - 1];
        var word = command.Decorator.ToString().ToLowerInvariant();
        if (!WardleyWriter.SetDecorator(document, component, word, command.Present))
        {
            return Task.FromResult(CommandResult.Failure($"That element's '{word}' could not be changed."));
        }

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(
            new RestoreWardleyLineCommand(command.BodyPath, component.Line, before)));
    }
}

/// <summary>Puts the whole document back, byte for byte.</summary>
public sealed class RestoreWardleyDocumentCommandHandler : ICommandHandler<RestoreWardleyDocumentCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public RestoreWardleyDocumentCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(RestoreWardleyDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var replaced = document.ToText();

        // Replacing the lines in place rather than swapping the document keeps every session's
        // reference to it valid - the store hands one instance to every connection.
        if (document.Lines.Count > 0)
        {
            document.RemoveLines(1, (uint)document.Lines.Count);
        }

        var restored = WardleyDocument.Parse(command.Text);
        for (var index = 0; index < restored.Lines.Count; index++)
        {
            document.InsertLine((uint)index + 1, restored.Lines[index]);
        }

        _documents.Save(command.BodyPath);
        return Task.FromResult(CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, replaced)));
    }
}
