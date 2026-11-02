using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What every Wardley map command handler shares: load the map, check the command against the map as it is now, write
/// it as model changes the library plans into splices (<see cref="WardleyBodyEdit"/>), save, and hand back the undo.
/// </summary>
/// <remarks>
/// <para>
/// Preconditions are always checked against the document as it is now rather than trusted from when the command was
/// made, because undo and redo dispatch the same instance again later (Requirement 9.5).
/// </para>
/// <para>
/// <b>Every undo is the shared restore of the whole text</b> (Requirement 5.7): it gives back the bytes the edit was
/// made to, however much the edit moved, and the store the identities it held for them. The edit's changes are one undo
/// step however many they are (Requirement 5.5).
/// </para>
/// <para>
/// <b>No handler builds a statement's text or replaces a line</b> (Requirement 5.1, wardley-map-disl-fbl task 12): each
/// names the element it changes by the id the store keeps for it, which is the id the binding reads it under, and says
/// which of its values change.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class WardleyEditHandler<TCommand>(IWardleyDocumentStore documents) : ICommandHandler<TCommand>
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal of an edit whose element the map no longer has.</summary>
    protected const string Gone = WardleyEdit.Gone;

    /// <summary>The store, for the identities held beside each map.</summary>
    protected IWardleyDocumentStore Documents { get; } = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return RunAsync(command, cancellationToken);
    }

    /// <summary>Runs <paramref name="command"/> against its document.</summary>
    /// <remarks>
    /// The edit is made on a body of its own, opened on the cached text, so a refusal halfway through leaves nothing behind
    /// for the next save to write. A document that could not be read is not edited at all.
    /// </remarks>
    protected virtual async Task<CommandResult> RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await Documents.GetOrLoadAsync(command.BodyPath, cancellationToken);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                $"{IoPath.GetFileName(command.BodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}");
        }

        var before = entry.Document.Text;
        var identities = Documents.Identities(command.BodyPath);
        var body = WardleyBody.Edit(entry.Document, identities);
        var outcome = Apply(body, new WardleyEditContext(entry.Document, entry.Model, identities), command);
        if (!outcome.WasApplied)
        {
            return CommandResult.Failure(outcome.Refusal!);
        }

        if (outcome.IsUnchanged)
        {
            return CommandResult.Success();
        }

        Documents.Remember(command.BodyPath, body.Text, body.Identities());
        var saved = await Documents.SaveAsync(command.BodyPath, LineDocument.Parse(body.Text), cancellationToken);
        return CommandResult.FromSave(saved, new RestoreDocumentCommand<IWardleyDocumentStore>(command.BodyPath, before, command));
    }

    /// <summary>Changes <paramref name="body"/>, whose map and identities are <paramref name="map"/>, as <paramref name="command"/> asks, or says why not.</summary>
    protected abstract EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, TCommand command);

    /// <summary>Applies <paramref name="changes"/> in order, as one edit: the first refusal, else applied.</summary>
    protected static EditOutcome Changed(WardleyBodyEdit body, params ModelChange[] changes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            if (body.Change(change) is { WasApplied: false } refused) return refused;
        }

        return EditOutcome.Applied;
    }
}

/// <summary>The map an edit is checked against, with the identities the store keeps beside it.</summary>
/// <param name="Document">The map's text as it is now.</param>
/// <param name="Map">The map as it is now.</param>
/// <param name="Identities">Its elements' identities, which are the ids the binding reads them under.</param>
public sealed record WardleyEditContext(LineDocument Document, WardleyMap Map, IReadOnlyList<WardleyIdentityEntry> Identities)
{
    /// <summary>The map's model as the definition reads it, under the same identities.</summary>
    public DislDiagram Diagram => WardleyBody.Model(Document, Identities);

    /// <summary>The component an element id names, or null: the holder at the entry's ordinal, where two components carry one name (finding B9).</summary>
    public WardleyComponent? ComponentOf(string elementId) =>
        Identities.FirstOrDefault(candidate => candidate.Id == elementId && candidate.Kind == WardleyIdentityKind.Component) is { } entry
            ? WardleyHolders.Component(Map, entry.Key)
            : null;

    /// <summary>The id of the element of <paramref name="kind"/> declared on <paramref name="line"/>, or null.</summary>
    public string? IdAt(string kind, uint line) =>
        Map.Holders.FirstOrDefault(holder => holder.Kind == kind && holder.Line == line) is { } holder
            ? Identities.FirstOrDefault(entry => entry.Kind == kind && entry.Key == holder.Key)?.Id
            : null;

    /// <summary>Every name an element of the map has: its components' and its pipeline components'.</summary>
    public HashSet<string> Names() =>
        Map.Components.Select(component => component.Name)
            .Concat(Map.Pipelines.SelectMany(pipeline => pipeline.Children).Select(child => child.Name))
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>What the element handlers and the transaction's writer share besides their base.</summary>
internal static class WardleyEdit
{
    /// <summary>The refusal of an edit whose element the map no longer has.</summary>
    public const string Gone = "That element is no longer on this map.";

    /// <summary>A new element's id: a <c>ShortGuid</c>, as the definition's <c>ids.strategy</c> gives one.</summary>
    public static string NewId() => ShortGuid.NewShortGuid().ToString();

    /// <summary>
    /// Why the format cannot hold <paramref name="name"/>, or null when it can (finding B13): a <c>//</c> starts a comment,
    /// so the rest of the line is cut and the element is not what was typed; and a name a link to or from it is not read
    /// back with is a name the map can never link, so every "Link to" would append the link once more.
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
    }

    /// <summary>Whether a link from <paramref name="source"/> to <paramref name="target"/>, written through the binding, is read back as that link.</summary>
    private static bool ReadsBack(string source, string target)
    {
        var body = WardleyBody.Edit(LineDocument.Parse(""), []);
        var written = Component(body, "readback:source", source) && Component(body, "readback:target", target)
            && body.Change(new ModelChange.Add("Dependency", "readback:link", new Dictionary<string, object?>(StringComparer.Ordinal) { ["from"] = source, ["to"] = target })).WasApplied;
        return written
            && WardleyBody.Read(LineDocument.Parse(body.Text)).Model.Links is [var link]
            && link.Source == source && link.Target == target;
    }

    /// <summary>Whether an `evolve` names <paramref name="component"/>: it is the first holder of its name, and one names that name.</summary>
    public static bool Evolving(WardleyMap map, WardleyComponent component) =>
        WardleyHolders.IsFirstHolder(map, component) && map.Evolves.Any(evolve => evolve.Name == component.Name);

    private static bool Component(WardleyBodyEdit body, string id, string name) =>
        body.Change(new ModelChange.Add("Component", id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name, ["visibility"] = 0.5d, ["maturity"] = 0.5d })).WasApplied;

    /// <summary>
    /// The name a link, an `evolve` or a `pipeline` drawn from or to <paramref name="holder"/> is written with (finding B9,
    /// ruling 4): its own for the first holder of its name, which every such statement attaches to; for a later holder, a
    /// name of its own first, its name followed by a space and the lowest number from 2 that no element of the map has,
    /// written into its declaration in the same edit. Null when that declaration could not be rewritten.
    /// </summary>
    /// <param name="taken">The names the map has; the new name is added to it.</param>
    public static string? NameFor(WardleyBodyEdit body, WardleyEditContext map, WardleyComponent holder, string holderId, HashSet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(taken);
        if (WardleyHolders.IsFirstHolder(map.Map, holder))
        {
            return holder.Name;
        }

        var number = 2;
        string name;
        do
        {
            name = string.Create(CultureInfo.InvariantCulture, $"{holder.Name} {number++}");
        }
        while (!taken.Add(name));

        return body.Change(new ModelChange.Set(holderId, Attributes(("name", name)))).WasApplied ? name : null;
    }

    /// <summary>
    /// The link of <paramref name="kind"/> from <paramref name="source"/> to <paramref name="target"/>, or null: one naming
    /// both by their names, when each is the first holder of its name, which is the one such a link attaches to (ruling 3).
    /// </summary>
    public static WardleyLink? LinkBetween(WardleyMap map, WardleyComponent source, WardleyComponent target, WardleyLinkKind kind) =>
        WardleyHolders.IsFirstHolder(map, source) && WardleyHolders.IsFirstHolder(map, target)
            ? map.Links.FirstOrDefault(link => link.Source == source.Name && link.Target == target.Name && link.Kind == kind)
            : null;

    /// <summary>The attributes of a model change, by the binding's names.</summary>
    public static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] values)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in values) attributes[name] = value;
        return attributes;
    }

    /// <summary>The binding's type of a positioned element of <paramref name="kind"/>: Component, Anchor or Submap.</summary>
    public static string TypeOf(WardleyElementKind kind) => kind switch
    {
        WardleyElementKind.Anchor => "Anchor",
        WardleyElementKind.Submap => "Submap",
        _ => "Component",
    };

    /// <summary>The words of <paramref name="element"/>'s decorators as written, the ones that are no decorator of this notation included.</summary>
    public static List<string> DecoratorWords(FblElement? element) =>
        element?.Attributes.GetValueOrDefault("decorators") is IEnumerable<object?> words ? [.. words.OfType<string>()] : [];
}

public sealed class AddWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyElementCommand>(documents)
{
    private static readonly string[] Kinds = ["component", "anchor", "submap"];

    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, AddWardleyElementCommand command)
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

        // Two components sharing a name makes every reference to it ambiguous, which Requirement 14.4 reports as an
        // error - so refusing to create one is kinder than writing it and reporting it a moment later.
        if (map.Map.Components.Any(existing => existing.Name == command.Name))
        {
            return EditOutcome.Refused($"This map already has an element called '{command.Name}'.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        var attributes = WardleyEdit.Attributes(("name", command.Name), ("visibility", position.Visibility), ("maturity", position.Maturity));
        if (command.Decorator is { } decorator)
        {
            attributes["decorators"] = new List<object?> { WardleyDecorators.Spell(decorator) };
        }

        var type = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(command.Kind.ToLowerInvariant());
        return Changed(body, new ModelChange.Add(type, WardleyEdit.NewId(), attributes));
    }
}

public sealed class RemoveWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<RemoveWardleyElementCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, RemoveWardleyElementCommand command)
    {
        // Everything that names it goes with it, as the binding's remove cascades to the links and the evolutions that name
        // it, or the document is left referring to something that does not exist; its pipeline stays. A later holder of a
        // repeated name takes its own statement only, because nothing attaches to it (finding B9, ruling 6).
        return map.ComponentOf(command.ElementId) is null
            ? EditOutcome.Refused(Gone)
            : Changed(body, new ModelChange.Remove(command.ElementId));
    }
}

public sealed class RenameWardleyElementCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<RenameWardleyElementCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, RenameWardleyElementCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NewName))
        {
            return EditOutcome.Refused("An element needs a name.");
        }

        if (map.ComponentOf(command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        if (WardleyEdit.Unusable(command.NewName) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        if (WardleyEdit.Evolving(map.Map, component) && WardleyEvolveNames.OfComponent(command.NewName) is { } unevolvable)
        {
            return EditOutcome.Refused(unevolvable);
        }

        if (map.Map.Components.Any(existing => existing.Name == command.NewName))
        {
            return EditOutcome.Refused($"This map already has an element called '{command.NewName}'.");
        }

        // One change, whose references the library rewrites with it: every link, evolution, pipeline and pipeline
        // component naming the element, in the same edit (Requirement 5.5). Of a repeated name only the selected holder is
        // renamed (ruling 5), since the statements naming it attach to the first. The ids move with their keys (finding B10).
        return body.Change(new ModelChange.Set(command.ElementId, WardleyEdit.Attributes(("name", command.NewName)))).WasApplied
            ? EditOutcome.Applied
            : EditOutcome.Refused("That element could not be renamed.");
    }
}

public sealed class SetWardleyInertiaCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyInertiaCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyInertiaCommand command)
    {
        if (map.ComponentOf(command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        if (component.Inertia == command.Inertia)
        {
            return EditOutcome.Unchanged;
        }

        return body.Change(new ModelChange.Set(command.ElementId, WardleyEdit.Attributes(("inertia", command.Inertia ? "true" : "false")))).WasApplied
            ? EditOutcome.Applied
            : EditOutcome.Refused("That element's inertia could not be changed.");
    }
}

public sealed class SetWardleyDecoratorsCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyDecoratorsCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyDecoratorsCommand command)
    {
        if (map.ComponentOf(command.ElementId) is null)
        {
            return EditOutcome.Refused(Gone);
        }

        // The words as written, with each of the five decorators kept, removed or added as the command asks: a word that is
        // none of them is kept where it stands (Requirement 5.2).
        var wanted = command.Decorators.Distinct().Select(WardleyDecorators.Spell).ToList();
        var words = WardleyEdit.DecoratorWords(body.Reading.Elements.FirstOrDefault(element => element.Id == command.ElementId));
        var kept = words.Where(word => !IsDecorator(word) || wanted.Contains(word, StringComparer.OrdinalIgnoreCase)).ToList();
        kept.AddRange(wanted.Where(word => !kept.Contains(word, StringComparer.OrdinalIgnoreCase)));
        if (kept.SequenceEqual(words, StringComparer.Ordinal))
        {
            return EditOutcome.Unchanged;
        }

        return body.Change(new ModelChange.Set(command.ElementId, WardleyEdit.Attributes(("decorators", kept.Cast<object?>().ToList())))).WasApplied
            ? EditOutcome.Applied
            : EditOutcome.Refused("That element's decorators could not be changed.");
    }

    private static bool IsDecorator(string word) =>
        WardleyDecorators.All.Any(decorator => string.Equals(WardleyDecorators.Spell(decorator), word, StringComparison.OrdinalIgnoreCase));
}

public sealed class SetWardleyDecoratorCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyDecoratorCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyDecoratorCommand command)
    {
        if (map.ComponentOf(command.ElementId) is null)
        {
            return EditOutcome.Refused(Gone);
        }

        var word = WardleyDecorators.Spell(command.Decorator);
        var words = WardleyEdit.DecoratorWords(body.Reading.Elements.FirstOrDefault(element => element.Id == command.ElementId));
        var has = words.Contains(word, StringComparer.OrdinalIgnoreCase);
        if (has == command.Present)
        {
            return EditOutcome.Unchanged;
        }

        List<object?> now = command.Present
            ? [.. words, word]
            : [.. words.Where(candidate => !string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))];
        return body.Change(new ModelChange.Set(command.ElementId, WardleyEdit.Attributes(("decorators", now)))).WasApplied
            ? EditOutcome.Applied
            : EditOutcome.Refused($"That element's '{word}' could not be changed.");
    }
}

public sealed class AddWardleyNoteCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyNoteCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, AddWardleyNoteCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            // A note's text is also the only handle its identity has, so an empty one could not be found again even by ADP itself.
            return EditOutcome.Refused("A note needs something to say.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));
        return Changed(body, new ModelChange.Add("Note", WardleyEdit.NewId(), WardleyEdit.Attributes(("text", command.Text.Trim()), ("visibility", position.Visibility), ("maturity", position.Maturity))));
    }
}

public sealed class AddWardleyAnnotationCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<AddWardleyAnnotationCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, AddWardleyAnnotationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return EditOutcome.Refused("An annotation needs something to say.");
        }

        var position = WardleyAxis.Clamp(new WardleyCoordinate(command.Visibility, command.Maturity));

        // One past the highest, rather than one past the count: a map whose annotation 2 was deleted still has a 3, and
        // reusing that number would put two of them on the map.
        var number = map.Map.Annotations.Count == 0 ? 1 : map.Map.Annotations.Max(annotation => annotation.Number) + 1;
        var positions = new List<object?> { WardleyEdit.Attributes(("visibility", position.Visibility), ("maturity", position.Maturity)) };
        return Changed(body, new ModelChange.Add("Annotation", WardleyEdit.NewId(), WardleyEdit.Attributes(("number", (long)number), ("positions", positions), ("text", command.Text.Trim()))));
    }
}

public sealed class SetWardleyLinkCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyLinkCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyLinkCommand command)
    {
        var source = map.ComponentOf(command.SourceElementId);
        var target = map.ComponentOf(command.TargetElementId);
        if (source is null || target is null)
        {
            return EditOutcome.Refused(Gone);
        }

        if (source.Line == target.Line)
        {
            return EditOutcome.Refused("A link needs two different elements.");
        }

        // Matched on both endpoints AND the kind, because the DSL lets one pair carry a dependency and a flow at once, and
        // they are two different claims about the pair. A link names its ends' first holders (ruling 3), so a later holder
        // of a repeated name has none yet.
        var existing = WardleyEdit.LinkBetween(map.Map, source, target, command.Kind);
        var existingId = existing is null ? null : map.IdAt(WardleyIdentityKind.Link, existing.Line);

        if (!command.Present)
        {
            return existingId is null
                ? EditOutcome.Refused("There is no such link between these two elements.")
                : Changed(body, new ModelChange.Remove(existingId));
        }

        // An existing link has its context set rather than a second one being appended: two statements saying the same
        // thing is what Requirement 14.4 reports.
        var context = command.Context.Trim();
        if (existingId is not null)
        {
            return existing!.Context == context
                ? EditOutcome.Unchanged
                : Changed(body, new ModelChange.Set(existingId, WardleyEdit.Attributes(("context", context.Length > 0 ? context : null))));
        }

        // A later holder of a repeated name is given a name of its own first, in this same edit, because a link can only
        // be written as a name (ruling 4). The end the gesture starts from is named first.
        var taken = map.Names();
        if (WardleyEdit.NameFor(body, map, source, command.SourceElementId, taken) is not { } sourceName
            || WardleyEdit.NameFor(body, map, target, command.TargetElementId, taken) is not { } targetName)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        if (sourceName == targetName)
        {
            return EditOutcome.Refused("A link needs two different elements.");
        }

        // A link names its ends by their names, as the format writes them; the binding reads it back to the first holders.
        var attributes = WardleyEdit.Attributes(("from", sourceName), ("to", targetName));
        if (context.Length > 0) attributes["context"] = context;
        return Changed(body, new ModelChange.Add(command.Kind == WardleyLinkKind.Flow ? "Flow" : "Dependency", WardleyEdit.NewId(), attributes));
    }
}

public sealed class SetWardleyEvolveCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<SetWardleyEvolveCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyEvolveCommand command)
    {
        if (map.ComponentOf(command.ElementId) is not { } component)
        {
            return EditOutcome.Refused(Gone);
        }

        // A later holder of a repeated name has no `evolve`: one naming it attaches to the first holder (ruling 3).
        var existing = WardleyHolders.IsFirstHolder(map.Map, component)
            ? map.Map.Evolves.FirstOrDefault(evolve => evolve.Name == component.Name)
            : null;
        var existingId = existing is null ? null : body.Reading.Elements.FirstOrDefault(element => element.Type == "Evolve" && element.Line == existing.Line)?.Id;
        if (!command.Present)
        {
            return existingId is null
                ? EditOutcome.Refused($"'{component.Name}' is not evolving.")
                : Changed(body, new ModelChange.Remove(existingId));
        }

        var becomes = command.OverrideName.Trim();
        if (becomes.Length > 0 && WardleyEdit.Unusable(becomes) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        if (WardleyEvolveNames.Arriving(becomes) is { } unarriving)
        {
            return EditOutcome.Refused(unarriving);
        }

        // Clamped rather than refused, for the same reason a drag is: the axis has ends, and a value past one of them is a
        // slip rather than a different intention (Requirement 7.3).
        var maturity = Math.Clamp(command.Maturity, 0d, 1d);
        if (existingId is not null)
        {
            var changed = WardleyEdit.Attributes(("becomes", becomes.Length > 0 ? becomes : null));
            if (existing!.Maturity != maturity) changed["maturity"] = maturity;
            if (existing.Override == becomes) changed.Remove("becomes");
            return changed.Count == 0 ? EditOutcome.Unchanged : Changed(body, new ModelChange.Set(existingId, changed));
        }

        // A later holder of a repeated name is given a name of its own first, in this same edit, because an `evolve` can
        // only name it (ruling 4).
        if (WardleyEdit.NameFor(body, map, component, command.ElementId, map.Names()) is not { } name)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        if (WardleyEvolveNames.OfComponent(name) is { } unevolvable)
        {
            return EditOutcome.Refused(unevolvable);
        }

        var attributes = WardleyEdit.Attributes(("component", name), ("maturity", maturity));
        if (becomes.Length > 0) attributes["becomes"] = becomes;
        return Changed(body, new ModelChange.Add("Evolve", null, attributes));
    }
}

/// <summary>Pipeline membership: the one edit in this module that writes a block rather than a line.</summary>
public sealed class SetWardleyPipelineMembershipCommandHandler(IWardleyDocumentStore documents)
    : WardleyEditHandler<SetWardleyPipelineMembershipCommand>(documents)
{
    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, SetWardleyPipelineMembershipCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ChildName))
        {
            return EditOutcome.Refused("A pipeline component needs a name.");
        }

        if (map.ComponentOf(command.ParentElementId) is not { } parent)
        {
            return EditOutcome.Refused(Gone);
        }

        // A later holder of a repeated name has no pipeline: one naming it attaches to the first holder (ruling 3).
        var pipeline = WardleyHolders.IsFirstHolder(map.Map, parent)
            ? map.Map.Pipelines.FirstOrDefault(candidate => candidate.Parent == parent.Name)
            : null;
        var child = pipeline?.Children.FirstOrDefault(candidate => candidate.Name == command.ChildName);

        if (!command.Member)
        {
            return child is null || map.IdAt(WardleyIdentityKind.PipelineChild, child.Line) is not { } childId
                ? EditOutcome.Refused($"'{command.ChildName}' is not in this pipeline.")
                : Changed(body, new ModelChange.Remove(childId));
        }

        if (child is not null)
        {
            return EditOutcome.Refused($"'{command.ChildName}' is already in this pipeline.");
        }

        if (WardleyEdit.Unusable(command.ChildName) is { } unusable)
        {
            return EditOutcome.Refused(unusable);
        }

        // The legacy two-coordinate form has no block to put a child in, and Requirement 3.2 says a map written that way is
        // written back that way - so this refuses rather than silently converting the statement to the nested form.
        if (pipeline is { Form: WardleyPipelineForm.Legacy })
        {
            return EditOutcome.Refused(
                $"'{parent.Name}' has a pipeline written in the older two-coordinate form, which holds no components. Rewrite it as a nested pipeline first.");
        }

        var maturity = Math.Clamp(command.Maturity, 0d, 1d);
        var member = WardleyEdit.Attributes(("name", command.ChildName), ("maturity", maturity));

        if (pipeline is null)
        {
            // No pipeline yet: the parent gains one, written in the nested form, with its brace on its own line, because
            // that is the form that can hold what is being added - and a later holder of a repeated name a name of its own
            // first, in this same edit (ruling 4).
            if (WardleyEdit.NameFor(body, map, parent, command.ParentElementId, map.Names()) is not { } name)
            {
                return EditOutcome.Refused("That element could not be renamed.");
            }

            var started = WardleyEdit.NewId();
            return Changed(
                body,
                new ModelChange.Add("Pipeline", started, WardleyEdit.Attributes(("component", name))),
                new ModelChange.Add("PipelineComponent", WardleyEdit.NewId(), member, started));
        }

        if (!WardleyBody.Closes(LineDocument.Parse(body.Text), pipeline.Line))
        {
            // A nested pipeline whose block never closes is a document ADP did not write and cannot safely add to: guessing
            // where the block ends would put the child in whatever follows.
            return EditOutcome.Refused($"'{parent.Name}' has a pipeline that is missing its closing brace.");
        }

        return map.IdAt(WardleyIdentityKind.Pipeline, pipeline.Line) is { } pipelineId
            ? Changed(body, new ModelChange.Add("PipelineComponent", WardleyEdit.NewId(), member, pipelineId))
            : EditOutcome.Refused(Gone);
    }
}
