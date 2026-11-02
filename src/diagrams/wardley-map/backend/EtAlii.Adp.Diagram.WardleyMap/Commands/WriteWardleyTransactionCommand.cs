using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Writes what a transaction of the definition says (DISL §9.3) - an entry of a menu run, or a row committed - into the map,
/// each of its changes as the model change the library plans into splices through the binding (wardley-map-disl-fbl task 12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Its ids are the definition's</b>: an element the transaction creates has the id the runtime gave it when the entry
/// ran, so a redo creates it under the same id again. Every other id is the store's identity of the element, which is the
/// id the binding reads it under, but for an evolution, which keeps none (<see cref="WardleyBodyEdit.ReadingIdOf"/>).
/// </para>
/// <para>
/// What replaced it, <c>WardleyTransactionCommands</c>, translated a transaction onto the element commands, each with its
/// own writer; it left the product in task 12 and stays beside the tests.
/// </para>
/// </remarks>
/// <param name="BodyPath">The <c>.owm</c> file to edit.</param>
/// <param name="Operation">What made the transaction, as the refusal of one this map cannot write names it.</param>
/// <param name="Changes">The transaction's changes, in order.</param>
public sealed record WriteWardleyTransactionCommand(string BodyPath, string Operation, IReadOnlyList<DislChange> Changes) : IDocumentEditCommand;

/// <summary>Writes a transaction's changes as one edit, checked against the map as it is now.</summary>
public sealed class WriteWardleyTransactionCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<WriteWardleyTransactionCommand>(documents)
{
    /// <summary>The refusal of a transaction of <paramref name="operation"/> that this map cannot write.</summary>
    public static string Unwritable(string operation) =>
        $"The definition's {operation} makes a change this map's commands cannot write.";

    protected override EditOutcome Apply(WardleyBodyEdit body, WardleyEditContext map, WriteWardleyTransactionCommand command) =>
        new WardleyTransactionWriter(body, map, command.Operation).Write(command.Changes);
}

/// <summary>
/// One transaction written into one body: each change checked as the map's own commands check it, and written through the
/// binding.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the format cannot hold is refused before anything is written</b>: a name the map cannot read back
/// (<see cref="WardleyEdit.Unusable"/>), a second element of one name, a link of an element to itself, a component added to
/// a pipeline of the older form or one whose block does not close. A coordinate past an end of its axis is clamped, as a
/// drag is (Requirement 7.3).
/// </para>
/// <para>
/// <b>A statement that names an element names its first holder</b> (ruling 3), so a link, an evolution or a pipeline of a
/// later holder of a repeated name gives that holder a name of its own first, in the same edit (ruling 4).
/// </para>
/// <para>
/// <b>Only what changes is written</b>: a set of a value the element already has writes nothing for it, so the bytes it
/// was written with stay (Requirement 5.2), and a word of the decorators that is no decorator of this notation is kept
/// where it stands.
/// </para>
/// </remarks>
internal sealed class WardleyTransactionWriter(WardleyBodyEdit body, WardleyEditContext map, string operation)
{
    private static readonly string[] Coordinates = ["visibility", "maturity"];

    private readonly DislDiagram _diagram = map.Diagram;
    private HashSet<string>? _taken;

    /// <summary>The names the map has, with every name this transaction gave.</summary>
    private HashSet<string> Taken => _taken ??= map.Names();

    /// <summary>Writes <paramref name="changes"/> in order: the first refusal, else applied, or unchanged when none changed anything.</summary>
    public EditOutcome Write(IReadOnlyList<DislChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var changed = false;
        foreach (var change in changes)
        {
            EditOutcome outcome;
            try
            {
                outcome = Write(change);
            }
            catch (ArgumentException)
            {
                // A change the runtime cannot map onto this file's types: no command of this map writes it.
                return EditOutcome.Refused(WriteWardleyTransactionCommandHandler.Unwritable(operation));
            }

            if (!outcome.WasApplied) return outcome;
            changed |= !outcome.IsUnchanged;
        }

        return changed ? EditOutcome.Applied : EditOutcome.Unchanged;
    }

    private EditOutcome Write(DislChange change) => change switch
    {
        DislChange.Create { Type: "Component" or "Anchor" or "Submap" } create => Positioned(create),
        DislChange.Create { Type: "Evolution" } create => Referring(create, "component"),
        DislChange.Create { Type: "Pipeline" } create => Referring(create, "component"),
        DislChange.Create { Type: "PipelineComponent" } create => Member(create),
        DislChange.Create create => Added(create),
        DislChange.Connect connect => Connected(connect),
        DislChange.Set set => Set(set),
        DislChange.Remove remove => Removed(remove),
        _ => EditOutcome.Refused(WriteWardleyTransactionCommandHandler.Unwritable(operation)),
    };

    private EditOutcome Positioned(DislChange.Create create)
    {
        var name = Text(create.Attributes, "name");
        if (string.IsNullOrWhiteSpace(name)) return EditOutcome.Refused("An element needs a name.");
        if (WardleyEdit.Unusable(name) is { } unusable) return EditOutcome.Refused(unusable);

        // Two components sharing a name makes every reference to it ambiguous, which Requirement 14.4 reports as an error -
        // so refusing to create one is kinder than writing it and reporting it a moment later.
        if (map.Map.Components.Any(existing => existing.Name == name)) return EditOutcome.Refused($"This map already has an element called '{name}'.");

        Taken.Add(name);
        return Added(create with { Attributes = Clamped(create.Attributes) });
    }

    /// <summary>A statement that names a component by its name: an evolution or a pipeline.</summary>
    private EditOutcome Referring(DislChange.Create create, string attribute)
    {
        var id = IdOf(create.Attributes.GetValueOrDefault(attribute));
        if (map.ComponentOf(id) is not { } component) return EditOutcome.Refused(WardleyEdit.Gone);

        var becomes = Text(create.Attributes, "renamedTo").Trim();
        if (becomes.Length > 0 && WardleyEdit.Unusable(becomes) is { } unusable) return EditOutcome.Refused(unusable);
        if (WardleyEdit.NameFor(body, map, component, id, Taken) is not { } name) return EditOutcome.Refused("That element could not be renamed.");
        if (create.Type == "Evolution" && (WardleyEvolveNames.OfComponent(name) ?? WardleyEvolveNames.Arriving(becomes)) is { } unevolvable) return EditOutcome.Refused(unevolvable);

        // The statement names the component by its name, as the format writes it.
        var attributes = Clamped(create.Attributes);
        attributes[attribute] = name;
        if (create.Attributes.ContainsKey("renamedTo")) attributes["renamedTo"] = becomes;
        return Added(create with { Attributes = attributes });
    }

    private EditOutcome Member(DislChange.Create create)
    {
        var name = Text(create.Attributes, "name");
        if (string.IsNullOrWhiteSpace(name)) return EditOutcome.Refused("A pipeline component needs a name.");
        if (WardleyEdit.Unusable(name) is { } unusable) return EditOutcome.Refused(unusable);

        // A pipeline this transaction starts is written in the nested form, which holds what is added to it.
        var pipeline = map.Map.Pipelines.FirstOrDefault(candidate => map.IdAt(WardleyIdentityKind.Pipeline, candidate.Line) == create.ParentId);
        if (pipeline is not null)
        {
            if (pipeline.Children.Any(child => child.Name == name)) return EditOutcome.Refused($"'{name}' is already in this pipeline.");

            // The legacy two-coordinate form has no block to put a child in, and Requirement 3.2 says a map written that
            // way is written back that way - so this refuses rather than silently converting the statement.
            if (pipeline.Form == WardleyPipelineForm.Legacy)
            {
                return EditOutcome.Refused(
                    $"'{pipeline.Parent}' has a pipeline written in the older two-coordinate form, which holds no components. Rewrite it as a nested pipeline first.");
            }

            // A nested pipeline whose block never closes is a document ADP did not write and cannot safely add to:
            // guessing where the block ends would put the child in whatever follows.
            if (!WardleyBody.Closes(map.Document, pipeline.Line)) return EditOutcome.Refused($"'{pipeline.Parent}' has a pipeline that is missing its closing brace.");
        }

        return Added(create with { Attributes = Clamped(create.Attributes) });
    }

    private EditOutcome Connected(DislChange.Connect connect)
    {
        var source = map.ComponentOf(connect.SourceId);
        var target = map.ComponentOf(connect.TargetId);
        if (source is null || target is null) return EditOutcome.Refused(WardleyEdit.Gone);
        if (source.Line == target.Line) return EditOutcome.Refused("A link needs two different elements.");

        // Matched on both endpoints AND the kind, because the DSL lets one pair carry a dependency and a flow at once, and
        // they are two different claims about the pair: one already there is not written twice (Requirement 14.4).
        var kind = connect.Type == "Flow" ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency;
        if (WardleyEdit.LinkBetween(map.Map, source, target, kind) is not null) return EditOutcome.Unchanged;

        // The end the gesture starts from is named first.
        if (WardleyEdit.NameFor(body, map, source, connect.SourceId, Taken) is not { } sourceName
            || WardleyEdit.NameFor(body, map, target, connect.TargetId, Taken) is not { } targetName)
        {
            return EditOutcome.Refused("That element could not be renamed.");
        }

        if (sourceName == targetName) return EditOutcome.Refused("A link needs two different elements.");

        // A link names its ends by their names, as the format writes them; the binding reads it back to the first holders.
        var written = (ModelChange.Add)DislWrite.ToFbl(WardleyDefinition.Specification, connect with { Id = connect.Id.Length > 0 ? connect.Id : WardleyEdit.NewId() });
        var attributes = Stated(written.Attributes);
        attributes["from"] = sourceName;
        attributes["to"] = targetName;
        return body.Change(written with { Attributes = attributes });
    }

    private EditOutcome Added(DislChange.Create create)
    {
        var written = (ModelChange.Add)DislWrite.ToFbl(WardleyDefinition.Specification, create with { Id = create.Id.Length > 0 ? create.Id : WardleyEdit.NewId() });

        // A new pipeline is written in the nested form, the one that holds what is added to it.
        var type = create.Type == "Pipeline" ? "Pipeline" : written.Type;
        return body.Change(written with { Type = type, Attributes = Stated(Typed(Trimmed(written.Attributes))) });
    }

    private EditOutcome Set(DislChange.Set set)
    {
        if (_diagram.ElementById(set.ElementId) is not { } element || body.ReadingIdOf(element) is not { } id)
        {
            return EditOutcome.Refused(WardleyEdit.Gone);
        }

        var attributes = Clamped(set.Attributes);
        if (element.IsA("Positioned") && attributes.TryGetValue("name", out var renamed) && renamed is string name && name != element.ValueOf("name") as string)
        {
            if (string.IsNullOrWhiteSpace(name)) return EditOutcome.Refused("An element needs a name.");
            if (WardleyEdit.Unusable(name) is { } unusable) return EditOutcome.Refused(unusable);
            if (map.Map.Components.Any(existing => existing.Name == name)) return EditOutcome.Refused($"This map already has an element called '{name}'.");
            if (map.ComponentOf(set.ElementId) is { } component && WardleyEdit.Evolving(map.Map, component)
                && WardleyEvolveNames.OfComponent(name) is { } unevolvable) return EditOutcome.Refused(unevolvable);
            Taken.Add(name);
        }

        if (attributes.TryGetValue("renamedTo", out var becomes) && becomes is string text)
        {
            if (text.Trim() is { Length: > 0 } trimmed && WardleyEdit.Unusable(trimmed) is { } unusable) return EditOutcome.Refused(unusable);
            if (WardleyEvolveNames.Arriving(text.Trim()) is { } unarriving) return EditOutcome.Refused(unarriving);
            attributes["renamedTo"] = text.Trim();
        }

        // Only what changes: the rest keeps the bytes it was written with.
        foreach (var unchanged in attributes.Where(attribute => Same(element.ValueOf(attribute.Key), attribute.Value)).Select(attribute => attribute.Key).ToList())
        {
            attributes.Remove(unchanged);
        }

        if (attributes.Count == 0) return EditOutcome.Unchanged;

        var written = (ModelChange.Set)DislWrite.ToFbl(WardleyDefinition.Specification, set with { Attributes = attributes });
        var values = Typed(Trimmed(written.Attributes));
        if (values.ContainsKey("decorators")) values["decorators"] = Decorators(id, values["decorators"]);

        // An optional part set to nothing is left out of the statement.
        foreach (var emptied in values.Where(value => value.Value is "").Select(value => value.Key).ToList())
        {
            values[emptied] = null;
        }

        return body.Change(new ModelChange.Set(id, values));
    }

    private EditOutcome Removed(DislChange.Remove remove) =>
        _diagram.ElementById(remove.ElementId) is { } element && body.ReadingIdOf(element) is { } id
            ? body.Change(new ModelChange.Remove(id))
            : EditOutcome.Refused(WardleyEdit.Gone);

    /// <summary>
    /// The words of the decorators of the element read as <paramref name="id"/> once set to <paramref name="value"/>: each of
    /// the five decorators kept, removed or added as <paramref name="value"/> says, in the order they were written, and any
    /// other word kept where it stands (Requirement 5.2).
    /// </summary>
    private List<object?> Decorators(string id, object? value)
    {
        var wanted = value is IEnumerable<object?> list ? list.OfType<string>().ToList() : [];
        var words = WardleyEdit.DecoratorWords(body.Reading.Elements.FirstOrDefault(element => element.Id == id));
        var kept = words.Where(word => !IsDecorator(word) || wanted.Contains(word, StringComparer.OrdinalIgnoreCase)).ToList();
        kept.AddRange(wanted.Where(word => IsDecorator(word) && !kept.Contains(word, StringComparer.OrdinalIgnoreCase)));
        return [.. kept];
    }

    private static bool IsDecorator(string word) =>
        WardleyDecorators.All.Any(decorator => string.Equals(WardleyDecorators.Spell(decorator), word, StringComparison.OrdinalIgnoreCase));

    /// <summary><paramref name="attributes"/> with each coordinate within its axis, and without what the module adds to the model for its own reading.</summary>
    private static Dictionary<string, object?> Clamped(IReadOnlyDictionary<string, object?> attributes)
    {
        var clamped = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in attributes)
        {
            if (name is WardleyFunctions.WrittenSource or WardleyFunctions.WrittenTarget or WardleyFunctions.WrittenComponent or WardleyFunctions.WrittenDecorators) continue;
            clamped[name] = Coordinates.Contains(name) && value is double number ? Math.Clamp(number, 0d, 1d) : value;
        }

        return clamped;
    }

    /// <summary>A text, a context and a name an element arrives under without the spaces around them, as the map writes them.</summary>
    private static Dictionary<string, object?> Trimmed(IReadOnlyDictionary<string, object?> attributes) =>
        attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Key is "text" or "context" or "becomes" && attribute.Value is string text ? text.Trim() : attribute.Value,
            StringComparer.Ordinal);

    /// <summary>Values in the form the binding writes them: a flag as its word.</summary>
    private static Dictionary<string, object?> Typed(IReadOnlyDictionary<string, object?> attributes) =>
        attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value is bool flag ? (flag ? "true" : "false") : attribute.Value,
            StringComparer.Ordinal);

    /// <summary>The attributes a new statement states: none that is empty, and no inertia it does not have, which the statement leaves out.</summary>
    private static Dictionary<string, object?> Stated(IReadOnlyDictionary<string, object?> attributes) =>
        attributes.Where(attribute => attribute.Value is not (null or "" or IReadOnlyCollection<object?> { Count: 0 }) && !(attribute.Key == "inertia" && attribute.Value is "false"))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

    private static bool Same(object? now, object? then) => (now, then) switch
    {
        (double left, double right) => left == right,
        (long left, long right) => left == right,
        (IEnumerable<object?> left, IEnumerable<object?> right) when now is not string && then is not string => left.SequenceEqual(right),
        _ => Equals(now, then),
    };

    /// <summary>The id of the element a reference names, as an element or by its id; empty for none.</summary>
    private string IdOf(object? reference) => reference switch
    {
        DislElement element => element.Id,
        string id when _diagram.ElementById(id) is not null => id,
        _ => "",
    };

    private static string Text(IReadOnlyDictionary<string, object?> attributes, string name) =>
        attributes.GetValueOrDefault(name) as string ?? "";
}
