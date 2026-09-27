using System.Globalization;
using EtAlii.Adp.History;

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
        var decorator = command.Decorator is { } word ? $" ({word.ToString().ToLowerInvariant()})" : "";
        WardleyWriter.Append(
            document,
            $"{command.Kind.ToLowerInvariant()} {command.Name} [{WardleyEdit.Number(position.Visibility)}, {WardleyEdit.Number(position.Maturity)}]{decorator}");

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
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

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
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

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
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

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success( new RestoreWardleyLineCommand(command.BodyPath, component.Line, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

public sealed class SetWardleyDecoratorsCommandHandler : ICommandHandler<SetWardleyDecoratorsCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyDecoratorsCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(SetWardleyDecoratorsCommand command, CancellationToken cancellationToken)
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

        // One line, captured once: several writer calls follow, and the inverse has to put the
        // line back as it was rather than as the sum of their opposites.
        var before = document.Lines[(int)component.Line - 1];
        var wanted = command.Decorators.Distinct().ToArray();

        foreach (var decorator in WardleyDecorators.All)
        {
            var present = wanted.Contains(decorator);
            if (present == component.Decorators.Contains(decorator))
            {
                continue;
            }

            // Re-read after each change: the statement has been rewritten, so the component's
            // spans are from before it moved.
            var current = WardleyEdit.ComponentOf(
                WardleyParser.Parse(document), _documents.Identities(command.BodyPath), command.ElementId);

            if (current is null || !WardleyWriter.SetDecorator(document, current, decorator.ToString().ToLowerInvariant(), present))
            {
                return Task.FromResult(CommandResult.Failure("That element's decorators could not be changed."));
            }
        }

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success( new RestoreWardleyLineCommand(command.BodyPath, component.Line, before), published.Warning)
            : CommandResult.Failure(published.Error));
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

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success( new RestoreWardleyLineCommand(command.BodyPath, component.Line, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

public sealed class AddWardleyNoteCommandHandler : ICommandHandler<AddWardleyNoteCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public AddWardleyNoteCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(AddWardleyNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            // A note's text is also the only handle its identity has, so an empty one could not
            // be found again even by ADP itself.
            return Task.FromResult(CommandResult.Failure("A note needs something to say."));
        }

        var document = _documents.GetOrLoad(command.BodyPath);
        var before = document.ToText();
        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));

        WardleyWriter.Append(
            document,
            $"note {command.Text.Trim()} [{WardleyEdit.Number(position.Visibility)}, {WardleyEdit.Number(position.Maturity)}]");

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

public sealed class AddWardleyAnnotationCommandHandler : ICommandHandler<AddWardleyAnnotationCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public AddWardleyAnnotationCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(AddWardleyAnnotationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return Task.FromResult(CommandResult.Failure("An annotation needs something to say."));
        }

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var before = document.ToText();
        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));

        // One past the highest, rather than one past the count: a map whose annotation 2 was
        // deleted still has a 3, and reusing that number would put two of them on the map.
        var number = map.Annotations.Count == 0 ? 1 : map.Annotations.Max(annotation => annotation.Number) + 1;

        WardleyWriter.Append(
            document,
            $"annotation {number} [{WardleyEdit.Number(position.Visibility)}, {WardleyEdit.Number(position.Maturity)}] {command.Text.Trim()}");

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

public sealed class SetWardleyLinkCommandHandler : ICommandHandler<SetWardleyLinkCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyLinkCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(SetWardleyLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var identities = _documents.Identities(command.BodyPath);
        var source = WardleyEdit.ComponentOf(map, identities, command.SourceElementId);
        var target = WardleyEdit.ComponentOf(map, identities, command.TargetElementId);
        if (source is null || target is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        if (source.Name == target.Name)
        {
            return Task.FromResult(CommandResult.Failure("A link needs two different elements."));
        }

        // Matched on both endpoints AND the kind, because the DSL lets one pair carry a
        // dependency and a flow at once, and they are two different claims about the pair.
        var existing = map.Links.FirstOrDefault(link =>
            link.Source == source.Name && link.Target == target.Name && link.Kind == command.Kind);

        if (!command.Present && existing is null)
        {
            return Task.FromResult(CommandResult.Failure("There is no such link between these two elements."));
        }

        var before = document.ToText();
        if (!command.Present)
        {
            WardleyWriter.RemoveLine(document, existing!.Line);
        }
        else
        {
            var arrow = command.Kind == WardleyLinkKind.Flow ? "+>" : "->";
            var context = command.Context.Trim();
            var statement = context.Length > 0
                ? $"{source.Name}{arrow}{target.Name}; {context}"
                : $"{source.Name}{arrow}{target.Name}";

            // An existing link is rewritten in place rather than a second one being appended:
            // two statements saying the same thing is what Requirement 14.4 reports.
            if (existing is null)
            {
                WardleyWriter.Append(document, statement);
            }
            else
            {
                document.ReplaceLine(existing.Line, statement);
            }
        }

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

public sealed class SetWardleyEvolveCommandHandler : ICommandHandler<SetWardleyEvolveCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyEvolveCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(SetWardleyEvolveCommand command, CancellationToken cancellationToken)
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

        var existing = map.Evolves.FirstOrDefault(evolve => evolve.Name == component.Name);
        if (!command.Present && existing is null)
        {
            return Task.FromResult(CommandResult.Failure($"'{component.Name}' is not evolving."));
        }

        var before = document.ToText();
        if (!command.Present)
        {
            WardleyWriter.RemoveLine(document, existing!.Line);
        }
        else
        {
            // Clamped rather than refused, for the same reason a drag is: the axis has ends,
            // and a value past one of them is a slip rather than a different intention
            // (Requirement 7.3).
            var maturity = Math.Clamp(command.Maturity, 0d, 1d);
            var name = command.OverrideName.Trim().Length > 0
                ? $"{component.Name}->{command.OverrideName.Trim()}"
                : component.Name;
            var statement = $"evolve {name} {WardleyEdit.Number(maturity)}";

            if (existing is null)
            {
                WardleyWriter.Append(document, statement);
            }
            else
            {
                document.ReplaceLine(existing.Line, statement);
            }
        }

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}

/// <summary>
/// Pipeline membership, which is the one edit in this module that writes a block rather than a
/// line.
/// </summary>
public sealed class SetWardleyPipelineMembershipCommandHandler : ICommandHandler<SetWardleyPipelineMembershipCommand>
{
    private readonly IWardleyDocumentStore _documents;

    public SetWardleyPipelineMembershipCommandHandler(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public Task<CommandResult> ExecuteAsync(
        SetWardleyPipelineMembershipCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.ChildName))
        {
            return Task.FromResult(CommandResult.Failure("A pipeline component needs a name."));
        }

        var document = _documents.GetOrLoad(command.BodyPath);
        var map = WardleyParser.Parse(document);
        var parent = WardleyEdit.ComponentOf(map, _documents.Identities(command.BodyPath), command.ParentElementId);
        if (parent is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer on this map."));
        }

        var pipeline = map.Pipelines.FirstOrDefault(candidate => candidate.Parent == parent.Name);
        var child = pipeline?.Children.FirstOrDefault(candidate => candidate.Name == command.ChildName);

        if (!command.Member)
        {
            if (child is null)
            {
                return Task.FromResult(CommandResult.Failure($"'{command.ChildName}' is not in this pipeline."));
            }

            var removing = document.ToText();
            WardleyWriter.RemoveLine(document, child.Line);
            var publishedInner = _documents.Save(command.BodyPath, document);
            return Task.FromResult(!publishedInner.Failed
                ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, removing), publishedInner.Warning)
                : CommandResult.Failure(publishedInner.Error));
        }

        if (child is not null)
        {
            return Task.FromResult(CommandResult.Failure($"'{command.ChildName}' is already in this pipeline."));
        }

        // The legacy two-coordinate form has no block to put a child in, and Requirement 3.2
        // says a map written that way is written back that way - so this refuses rather than
        // silently converting the statement to the nested form.
        if (pipeline is { Form: WardleyPipelineForm.Legacy })
        {
            return Task.FromResult(CommandResult.Failure(
                $"'{parent.Name}' has a pipeline written in the older two-coordinate form, which holds no components. Rewrite it as a nested pipeline first."));
        }

        var before = document.ToText();
        var statement = $"  component {command.ChildName} [{WardleyEdit.Number(Math.Clamp(command.Maturity, 0d, 1d))}]";

        if (pipeline is null)
        {
            // No pipeline yet: the parent gains one, written in the nested form because that is
            // the form that can hold what is being added.
            WardleyWriter.Append(document, $"pipeline {parent.Name}");
            WardleyWriter.Append(document, "{");
            WardleyWriter.Append(document, statement);
            WardleyWriter.Append(document, "}");
        }
        else if (ClosingBraceOf(document, pipeline) is { } closing)
        {
            document.InsertLine(closing, statement);
        }
        else
        {
            // A nested pipeline whose block never closes is a document ADP did not write and
            // cannot safely add to: guessing where the block ends would put the child in
            // whatever follows.
            return Task.FromResult(CommandResult.Failure($"'{parent.Name}' has a pipeline that is missing its closing brace."));
        }

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, before), published.Warning)
            : CommandResult.Failure(published.Error));
    }

    /// <summary>
    /// The 1-based line holding the pipeline's closing brace, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Found by scanning rather than computed from the children, because a block may hold blank
    /// lines, comments and statements this module does not model - all of which Requirement 3.3
    /// says survive untouched, and any of which may sit after the last child.
    /// </remarks>
    private static uint? ClosingBraceOf(WardleyDocument document, WardleyPipeline pipeline)
    {
        for (var number = pipeline.Line; number <= document.Lines.Count; number++)
        {
            if (document.Lines[(int)number - 1].Trim() == "}")
            {
                return number;
            }
        }

        return null;
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

        var published = _documents.Save(command.BodyPath, document);
        return Task.FromResult(!published.Failed
            ? CommandResult.Success(new RestoreWardleyDocumentCommand(command.BodyPath, replaced), published.Warning)
            : CommandResult.Failure(published.Error));
    }
}
