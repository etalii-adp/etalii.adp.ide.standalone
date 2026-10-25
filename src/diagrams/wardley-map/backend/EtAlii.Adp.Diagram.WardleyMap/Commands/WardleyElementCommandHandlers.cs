using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The handlers for the element commands, on the shared edit run (code-structure FR-033): the
/// run loads the map, hands a copy of it here, saves it and reports the undo. A handler locates
/// against <b>current</b> state, refuses with a sentence when that fails, and edits through
/// <see cref="WardleyWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Preconditions are always checked against the document as it is now rather than trusted from
/// when the command was made, because undo and redo dispatch the same instance again later
/// (Requirement 9.5).
/// </para>
/// <para>
/// <b>Two kinds of undo.</b> An edit that touches several lines - an add, a remove, a rename - is
/// undone by the run's restore of the whole text. An edit of one line names its own undo, a
/// <see cref="RestoreWardleyLineCommand"/>, so that undoing a drag brings back <c>[0.90, 0.10]</c>
/// rather than <c>[0.9, 0.1]</c> and leaves the rest of the file to whoever wrote it since.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class WardleyEditHandler<TCommand>(IWardleyDocumentStore documents)
    : DocumentEditHandler<TCommand, IWardleyDocumentStore, LineDocument, WardleyMap>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal of an edit whose element the map no longer has.</summary>
    protected const string Gone = "That element is no longer on this map.";

    /// <summary>The store, for the identities held beside each map.</summary>
    protected IWardleyDocumentStore Documents { get; } = documents;

    /// <inheritdoc />
    protected sealed override WardleyMap Parse(LineDocument body) => WardleyParser.Parse(body);

    /// <summary>The component <paramref name="elementId"/> names on the map at <paramref name="bodyPath"/>, or null.</summary>
    protected WardleyComponent? ComponentOf(WardleyMap map, string bodyPath, string elementId) =>
        WardleyEdit.ComponentOf(map, Documents.Identities(bodyPath), elementId);
}

/// <summary>What the element handlers share besides their base.</summary>
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

        // The holder at the entry's ordinal, where two components carry one name (finding B9).
        return entry is null
            ? null
            : WardleyIdentityKeys.Component(map, entry.Key);
    }

    /// <summary>
    /// Why the format cannot hold <paramref name="name"/>, or null when it can (finding B13): a
    /// <c>//</c> starts a comment, so the rest of the line is cut and the element is not what was
    /// typed; and a name a link to or from it is not read back with is a name the map can never
    /// link, so every "Link to" would append the link once more.
    /// </summary>
    public static string? Unusable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Contains("//", StringComparison.Ordinal))
        {
            return "A name cannot hold '//', which the map reads as the start of a comment.";
        }

        const string other = "Other";
        return ReadsBack(name, other) && ReadsBack(other, name)
            ? null
            : $"A link to or from '{name}' could not be read back, so the map cannot hold that name.";

        static bool ReadsBack(string source, string target)
        {
            var map = WardleyParser.Parse(LineDocument.Parse(WardleyWriter.LinkStatement(source, target, WardleyLinkKind.Dependency, "") + "\n"));
            return map.Links is [var link] && link.Source == source && link.Target == target;
        }
    }

    /// <summary>
    /// The name a link, an `evolve` or a `pipeline` drawn from or to <paramref name="holder"/> is
    /// written with (finding B9, ruling 4): its own for the first holder of its name, which every
    /// such statement attaches to; for a later holder, a name of its own first, its name followed by
    /// a space and the lowest number from 2 that no element of the map has, written into its
    /// declaration in the same edit. Null when that declaration could not be rewritten.
    /// </summary>
    /// <param name="taken">The names the map has; the new name is added to it.</param>
    public static string? NameFor(LineDocument body, WardleyMap map, WardleyComponent holder, HashSet<string> taken)
    {
        if (WardleyIdentityKeys.IsFirstHolder(map, holder))
        {
            return holder.Name;
        }

        var number = 2;
        string name;
        do
        {
            name = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{holder.Name} {number++}");
        }
        while (!taken.Add(name));

        return WardleyWriter.RenameDeclaration(body, holder, name) ? name : null;
    }

    /// <summary>Every name an element of <paramref name="map"/> has: its components' and its pipeline components'.</summary>
    public static HashSet<string> Names(WardleyMap map) =>
        map.Components.Select(component => component.Name)
            .Concat(map.Pipelines.SelectMany(pipeline => pipeline.Children).Select(child => child.Name))
            .ToHashSet(StringComparer.Ordinal);
}

public sealed class AddWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyElementCommand>(documents)
{
    private static readonly string[] Kinds = ["component", "anchor", "submap"];

    protected override EditOutcome Apply(LineDocument body, WardleyMap model, AddWardleyElementCommand command)
    {
        if (!Kinds.Contains(command.Kind, StringComparer.OrdinalIgnoreCase))
        {
            return EditOutcome.Refused($"A Wardley map has no '{command.Kind}' statement.");
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return EditOutcome.Refused("An element needs a name.");
        }

        if (WardleyEdit.Unusable(command.Name) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        // Two components sharing a name makes every reference to it ambiguous, which
        // Requirement 14.4 reports as an error - so refusing to create one is kinder than
        // writing it and reporting it a moment later.
        if (model.Components.Any(existing => existing.Name == command.Name))
        {
            return EditOutcome.Refused($"This map already has an element called '{command.Name}'.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        WardleyWriter.Append(body, WardleyWriter.ElementStatement(command.Kind, command.Name, position, command.Decorator));
        return EditOutcome.Applied;
    }
}

public sealed class RemoveWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<RemoveWardleyElementCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, RemoveWardleyElementCommand command)
    {
        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        // Everything that names it goes with it, or the document is left referring to something
        // that does not exist. Descending, so removing one line cannot shift the next. A later
        // holder of a repeated name takes its own statement only, because nothing attaches to it
        // (finding B9, ruling 6).
        var lines = new List<uint> { component.Line };
        if (WardleyIdentityKeys.IsFirstHolder(model, component))
        {
            lines.AddRange(model.Links
                .Where(link => link.Source == component.Name || link.Target == component.Name)
                .Select(link => link.Line));
            lines.AddRange(model.Evolves.Where(evolve => evolve.Name == component.Name).Select(evolve => evolve.Line));
        }

        foreach (var line in lines.Distinct().OrderByDescending(line => line))
        {
            WardleyWriter.RemoveLine(body, line);
        }

        return EditOutcome.Applied;
    }
}

public sealed class RenameWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<RenameWardleyElementCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, RenameWardleyElementCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NewName))
        {
            return EditOutcome.Refused("An element needs a name.");
        }

        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        if (WardleyEdit.Unusable(command.NewName) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        if (model.Components.Any(existing => existing.Name == command.NewName))
        {
            return EditOutcome.Refused($"This map already has an element called '{command.NewName}'.");
        }

        // One pass over the declaration and every reference, because Requirement 9.6's undo only
        // restores them together if they moved together. Only the selected holder of a repeated
        // name is renamed (ruling 5). The identities follow by line when the store saves it, the
        // links, pipeline and pipeline components naming it included (finding B10).
        if (WardleyWriter.Rename(body, model, component, command.NewName) == 0)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        return EditOutcome.Applied;
    }
}

public sealed class SetWardleyInertiaCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyInertiaCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyInertiaCommand command)
    {
        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        var before = body.Lines[(int)component.Line - 1].Text;
        return WardleyWriter.SetInertia(body, component, command.Inertia)
            ? EditOutcome.UndoneBy(new RestoreWardleyLineCommand(command.BodyPath, component.Line, before))
            : EditOutcome.Refused("That element's inertia could not be changed.");
    }
}

public sealed class SetWardleyDecoratorsCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyDecoratorsCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyDecoratorsCommand command)
    {
        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        // One line, captured once: several writer calls follow, and the inverse has to put the
        // line back as it was rather than as the sum of their opposites.
        var before = body.Lines[(int)component.Line - 1].Text;
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
            var current = ComponentOf(WardleyParser.Parse(body), command.BodyPath, command.ElementId);
            if (current is null || !WardleyWriter.SetDecorator(body, current, decorator.ToString().ToLowerInvariant(), present))
            {
                return EditOutcome.Refused("That element's decorators could not be changed.");
            }
        }

        return EditOutcome.UndoneBy(new RestoreWardleyLineCommand(command.BodyPath, component.Line, before));
    }
}

public sealed class SetWardleyDecoratorCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyDecoratorCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyDecoratorCommand command)
    {
        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        var before = body.Lines[(int)component.Line - 1].Text;
        var word = command.Decorator.ToString().ToLowerInvariant();
        return WardleyWriter.SetDecorator(body, component, word, command.Present)
            ? EditOutcome.UndoneBy(new RestoreWardleyLineCommand(command.BodyPath, component.Line, before))
            : EditOutcome.Refused($"That element's '{word}' could not be changed.");
    }
}

public sealed class AddWardleyNoteCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyNoteCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, AddWardleyNoteCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            // A note's text is also the only handle its identity has, so an empty one could not
            // be found again even by ADP itself.
            return EditOutcome.Refused("A note needs something to say.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        WardleyWriter.Append(body, WardleyWriter.NoteStatement(command.Text, position));
        return EditOutcome.Applied;
    }
}

public sealed class AddWardleyAnnotationCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyAnnotationCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, AddWardleyAnnotationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return EditOutcome.Refused("An annotation needs something to say.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));

        // One past the highest, rather than one past the count: a map whose annotation 2 was
        // deleted still has a 3, and reusing that number would put two of them on the map.
        var number = model.Annotations.Count == 0 ? 1 : model.Annotations.Max(annotation => annotation.Number) + 1;

        WardleyWriter.Append(body, WardleyWriter.AnnotationStatement(number, position, command.Text));
        return EditOutcome.Applied;
    }
}

public sealed class SetWardleyLinkCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyLinkCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyLinkCommand command)
    {
        var source = ComponentOf(model, command.BodyPath, command.SourceElementId);
        var target = ComponentOf(model, command.BodyPath, command.TargetElementId);
        if (source is null || target is null)
        {
            return EditOutcome.Refused(Gone);
        }

        if (source.Line == target.Line)
        {
            return EditOutcome.Refused("A link needs two different elements.");
        }

        // Matched on both endpoints AND the kind, because the DSL lets one pair carry a
        // dependency and a flow at once, and they are two different claims about the pair.
        var existing = model.Links.FirstOrDefault(link =>
            link.Source == source.Name && link.Target == target.Name && link.Kind == command.Kind);

        if (!command.Present)
        {
            if (existing is null)
            {
                return EditOutcome.Refused("There is no such link between these two elements.");
            }

            WardleyWriter.RemoveLine(body, existing.Line);
            return EditOutcome.Applied;
        }

        // A later holder of a repeated name is given a name of its own first, in this same edit,
        // because a link can only be written as a name (ruling 4). The end the gesture starts from
        // is named first.
        var taken = WardleyEdit.Names(model);
        if (WardleyEdit.NameFor(body, model, source, taken) is not { } sourceName
            || WardleyEdit.NameFor(body, model, target, taken) is not { } targetName)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        if (sourceName == targetName)
        {
            return EditOutcome.Refused("A link needs two different elements.");
        }

        var statement = WardleyWriter.LinkStatement(sourceName, targetName, command.Kind, command.Context);

        // An existing link is rewritten in place rather than a second one being appended:
        // two statements saying the same thing is what Requirement 14.4 reports.
        if (existing is null)
        {
            WardleyWriter.Append(body, statement);
        }
        else
        {
            WardleyWriter.ReplaceLine(body, existing.Line, statement);
        }

        return EditOutcome.Applied;
    }
}

public sealed class SetWardleyEvolveCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyEvolveCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyEvolveCommand command)
    {
        if (ComponentOf(model, command.BodyPath, command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        // A later holder of a repeated name has no `evolve`: one naming it attaches to the first holder (ruling 3).
        var existing = WardleyIdentityKeys.IsFirstHolder(model, component)
            ? model.Evolves.FirstOrDefault(evolve => evolve.Name == component.Name)
            : null;
        if (!command.Present)
        {
            if (existing is null)
            {
                return EditOutcome.Refused($"'{component.Name}' is not evolving.");
            }

            WardleyWriter.RemoveLine(body, existing.Line);
            return EditOutcome.Applied;
        }

        if (command.OverrideName.Trim() is { Length: > 0 } becomes && WardleyEdit.Unusable(becomes) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        // A later holder of a repeated name is given a name of its own first, in this same edit,
        // because an `evolve` can only name it (ruling 4).
        if (WardleyEdit.NameFor(body, model, component, WardleyEdit.Names(model)) is not { } name)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        // Clamped rather than refused, for the same reason a drag is: the axis has ends, and a
        // value past one of them is a slip rather than a different intention (Requirement 7.3).
        var maturity = Math.Clamp(command.Maturity, 0d, 1d);
        var statement = WardleyWriter.EvolveStatement(name, command.OverrideName, maturity);

        if (existing is null)
        {
            WardleyWriter.Append(body, statement);
        }
        else
        {
            WardleyWriter.ReplaceLine(body, existing.Line, statement);
        }

        return EditOutcome.Applied;
    }
}

/// <summary>
/// Pipeline membership, which is the one edit in this module that writes a block rather than a
/// line.
/// </summary>
public sealed class SetWardleyPipelineMembershipCommandHandler(IWardleyDocumentStore documents)
    : WardleyEditHandler<SetWardleyPipelineMembershipCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, WardleyMap model, SetWardleyPipelineMembershipCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ChildName))
        {
            return EditOutcome.Refused("A pipeline component needs a name.");
        }

        if (ComponentOf(model, command.BodyPath, command.ParentElementId) is not { } parent)
        {
            return EditOutcome.Refused(Gone);
        }

        // A later holder of a repeated name has no pipeline: one naming it attaches to the first holder (ruling 3).
        var pipeline = WardleyIdentityKeys.IsFirstHolder(model, parent)
            ? model.Pipelines.FirstOrDefault(candidate => candidate.Parent == parent.Name)
            : null;
        var child = pipeline?.Children.FirstOrDefault(candidate => candidate.Name == command.ChildName);

        if (!command.Member)
        {
            if (child is null)
            {
                return EditOutcome.Refused($"'{command.ChildName}' is not in this pipeline.");
            }

            WardleyWriter.RemoveLine(body, child.Line);
            return EditOutcome.Applied;
        }

        if (child is not null)
        {
            return EditOutcome.Refused($"'{command.ChildName}' is already in this pipeline.");
        }

        if (WardleyEdit.Unusable(command.ChildName) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        // The legacy two-coordinate form has no block to put a child in, and Requirement 3.2
        // says a map written that way is written back that way - so this refuses rather than
        // silently converting the statement to the nested form.
        if (pipeline is { Form: WardleyPipelineForm.Legacy })
        {
            return EditOutcome.Refused(
                $"'{parent.Name}' has a pipeline written in the older two-coordinate form, which holds no components. Rewrite it as a nested pipeline first.");
        }

        var maturity = Math.Clamp(command.Maturity, 0d, 1d);

        if (pipeline is null)
        {
            // No pipeline yet: the parent gains one, written in the nested form because that is
            // the form that can hold what is being added - and a later holder of a repeated name
            // a name of its own first, in this same edit (ruling 4).
            if (WardleyEdit.NameFor(body, model, parent, WardleyEdit.Names(model)) is not { } name)
            {
                return EditOutcome.Refused("That element could not be renamed.");
            }

            foreach (var line in WardleyWriter.PipelineStatements(name, command.ChildName, maturity))
            {
                WardleyWriter.Append(body, line);
            }

            return EditOutcome.Applied;
        }

        if (ClosingBraceOf(body, pipeline) is not { } closing)
        {
            // A nested pipeline whose block never closes is a document ADP did not write and
            // cannot safely add to: guessing where the block ends would put the child in
            // whatever follows.
            return EditOutcome.Refused($"'{parent.Name}' has a pipeline that is missing its closing brace.");
        }

        WardleyWriter.InsertLine(body, closing, WardleyWriter.PipelineChildStatement(command.ChildName, maturity));
        return EditOutcome.Applied;
    }

    /// <summary>
    /// The 1-based line holding the pipeline's closing brace, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Found by scanning rather than computed from the children, because a block may hold blank
    /// lines, comments and statements this module does not model - all of which Requirement 3.3
    /// says survive untouched, and any of which may sit after the last child.
    /// </remarks>
    private static uint? ClosingBraceOf(LineDocument document, WardleyPipeline pipeline)
    {
        for (var number = pipeline.Line; number <= document.Lines.Count; number++)
        {
            if (document.Lines[(int)number - 1].Text.Trim() == "}")
            {
                return number;
            }
        }

        return null;
    }
}
