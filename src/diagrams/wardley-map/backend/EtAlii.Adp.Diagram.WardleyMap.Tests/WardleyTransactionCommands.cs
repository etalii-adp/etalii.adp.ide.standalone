using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The command of today's that writes what a transaction of the definition says (DISL §9.3): a create, a set of one
/// attribute, a connect or a remove, each onto the record whose handler splices the map's own lines. It stands in for
/// FBL's splices until the map is written through its binding, and goes in task 12 of wardley-map-disl-fbl.
/// </summary>
/// <remarks>
/// <para>
/// <b>An oracle, not the product</b>: what the module wrote and keyed with before wardley-map-disl-fbl task 12 put
/// every edit through the binding. It is kept beside the tests to check the binding's output against.
/// </para>
/// <para>
/// <b>Every element is named by its model id</b>, which is the store's identity for it (<see cref="WardleyBody"/>):
/// a statement that names an element by its name, a link's end, an evolution's or a pipeline's <c>component</c>, is
/// named by its first holder's.
/// </para>
/// <para>
/// <b>A transaction no command writes has none</b>, and is refused by the caller in a sentence naming the operation;
/// <c>WardleyDerivedOperations.Tests</c> runs every operation of the definition over the
/// corpus so that none ships that way.
/// </para>
/// </remarks>
internal static class WardleyTransactionCommands
{
    /// <summary>
    /// The command that writes <paramref name="transaction"/> into the map at <paramref name="bodyPath"/>, whose model
    /// before it ran is <paramref name="diagram"/>; null when no command writes it. <paramref name="row"/> says a
    /// property row's commit made it, whose set of the decorators is the whole set, where an operation's toggles one.
    /// </summary>
    public static ICommand? Of(string bodyPath, DislDiagram diagram, DislTransaction transaction, bool row = false)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied) return null;

        return transaction.Changes switch
        {
            [DislChange.Create { Type: "Pipeline" } started, DislChange.Create { Type: "PipelineComponent" } child] when child.ParentId == started.Id =>
                Member(bodyPath, IdOf(diagram, started.Attributes.GetValueOrDefault("component")), child),
            [DislChange.Create create] => Created(bodyPath, diagram, create),
            [DislChange.Set set] => Set(bodyPath, diagram, set, row),
            [DislChange.Set first, ..] when transaction.Changes.All(change => change is DislChange.Set set && set.ElementId == first.ElementId) =>
                Set(bodyPath, diagram, Merged(first, transaction.Changes.Cast<DislChange.Set>()), row),
            [DislChange.Connect connect] => Connected(bodyPath, connect),
            [.., DislChange.Remove removed] => Removed(bodyPath, diagram, transaction.Changes, removed),
            _ => null,
        };
    }

    private static ICommand? Created(string bodyPath, DislDiagram diagram, DislChange.Create create)
    {
        var attributes = create.Attributes;
        switch (create.Type)
        {
            case "Component" or "Anchor" or "Submap":
                {
                    WardleyDecorator?[] decorators = [.. Decorators(attributes.GetValueOrDefault("decorators")).Select(decorator => (WardleyDecorator?)decorator)];
                    if (decorators.Length > 1) return null;
                    return new AddWardleyElementCommand(
                        bodyPath,
                        create.Type.ToLowerInvariant(),
                        Text(attributes, "name"),
                        Number(attributes, "visibility"),
                        Number(attributes, "maturity"),
                        decorators.FirstOrDefault());
                }

            case "Note":
                return new AddWardleyNoteCommand(bodyPath, Text(attributes, "text"), Number(attributes, "visibility"), Number(attributes, "maturity"));

            case "Annotation":
                {
                    // A new annotation has one occurrence; its number is the handler's to give, as the definition's
                    // nextAnnotationNumber() gives it.
                    return attributes.GetValueOrDefault("positions") is IReadOnlyList<object?> and [IReadOnlyDictionary<string, object?> position]
                        && position.GetValueOrDefault("visibility") is double visibility && position.GetValueOrDefault("maturity") is double maturity
                        ? new AddWardleyAnnotationCommand(bodyPath, Text(attributes, "text"), visibility, maturity)
                        : null;
                }

            case "Evolution":
                return IdOf(diagram, attributes.GetValueOrDefault("component")) is { Length: > 0 } evolving
                    ? new SetWardleyEvolveCommand(bodyPath, evolving, Present: true, Number(attributes, "maturity"), Text(attributes, "renamedTo"))
                    : null;

            case "PipelineComponent":
                return diagram.ElementById(create.ParentId ?? "") is { Type.Name: "Pipeline" } pipeline
                    ? Member(bodyPath, IdOf(diagram, pipeline.ValueOf("component")), create)
                    : null;

            default:
                return null;
        }
    }

    private static SetWardleyPipelineMembershipCommand? Member(string bodyPath, string parentId, DislChange.Create child) =>
        parentId.Length > 0
            ? new SetWardleyPipelineMembershipCommand(bodyPath, parentId, Text(child.Attributes, "name"), Member: true, Number(child.Attributes, "maturity"))
            : null;

    private static ICommand? Set(string bodyPath, DislDiagram diagram, DislChange.Set set, bool row)
    {
        if (diagram.ElementById(set.ElementId) is not { } element) return null;
        var attributes = set.Attributes;

        if (element.IsA("Positioned"))
        {
            if (attributes.Count != 1) return null;
            (string name, object? value) = attributes.Single();
            if (name == "decorators") return Decorated(bodyPath, element, Decorators(value), row);
            return name switch
            {
                "name" => new RenameWardleyElementCommand(bodyPath, element.Id, Text(attributes, "name")),
                "visibility" => new MoveWardleyElementCommand(bodyPath, element.Id, Number(attributes, "visibility"), Number(element, "maturity")),
                "maturity" => new MoveWardleyElementCommand(bodyPath, element.Id, Number(element, "visibility"), Number(attributes, "maturity")),
                "inertia" when value is bool inertia => new SetWardleyInertiaCommand(bodyPath, element.Id, inertia),
                _ => null,
            };
        }

        if (attributes.Count != 1) return null;
        return (element.Type.Name, attributes.Keys.Single()) switch
        {
            // A pipeline component's visibility is its parent's, so only its maturity moves (the command knows that).
            ("PipelineComponent", "maturity") => new MoveWardleyElementCommand(bodyPath, element.Id, 0d, Number(attributes, "maturity")),
            ("Dependency" or "Flow", "context") => new SetWardleyLinkCommand(bodyPath, element.Source?.Id ?? "", element.Target?.Id ?? "", KindOf(element), Present: true, Text(attributes, "context")),
            ("Evolution", "maturity") => Evolved(bodyPath, diagram, element, Number(attributes, "maturity"), Text(element, "renamedTo")),
            ("Evolution", "renamedTo") => Evolved(bodyPath, diagram, element, Number(element, "maturity"), Text(attributes, "renamedTo")),
            _ => null,
        };
    }

    /// <summary>
    /// Several sets of one element as the one set they come to, each attribute at the value it was set to last: a
    /// write of one set action per attribute, as the Decorators row's is, sets them together.
    /// </summary>
    private static DislChange.Set Merged(DislChange.Set first, IEnumerable<DislChange.Set> sets)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            foreach ((string name, object? value) in set.Attributes) attributes[name] = value;
        }
        return first with { Attributes = attributes };
    }

    private static SetWardleyEvolveCommand? Evolved(string bodyPath, DislDiagram diagram, DislElement evolution, double maturity, string renamedTo) =>
        IdOf(diagram, evolution.ValueOf("component")) is { Length: > 0 } evolving
            ? new SetWardleyEvolveCommand(bodyPath, evolving, Present: true, maturity, renamedTo)
            : null;

    private static SetWardleyLinkCommand? Connected(string bodyPath, DislChange.Connect connect) => connect.Type switch
    {
        "Dependency" => new SetWardleyLinkCommand(bodyPath, connect.SourceId, connect.TargetId, WardleyLinkKind.Dependency, Present: true),
        "Flow" => new SetWardleyLinkCommand(bodyPath, connect.SourceId, connect.TargetId, WardleyLinkKind.Flow, Present: true),
        _ => null,
    };

    /// <summary>
    /// A removal: of an element, with what goes with it, which its command removes too (the handler takes the links and
    /// the evolution that name it, as the definition's <c>delete-referencing</c> says); of a link, an evolution or a
    /// pipeline component, on its own.
    /// </summary>
    private static ICommand? Removed(string bodyPath, DislDiagram diagram, IReadOnlyList<DislChange> changes, DislChange.Remove removed)
    {
        if (diagram.ElementById(removed.ElementId) is not { } element) return null;
        if (element.IsA("Positioned")) return new RemoveWardleyElementCommand(bodyPath, element.Id);
        if (changes.Count != 1) return null;

        return element.Type.Name switch
        {
            "Dependency" or "Flow" => new SetWardleyLinkCommand(bodyPath, element.Source?.Id ?? "", element.Target?.Id ?? "", KindOf(element), Present: false),
            "Evolution" => IdOf(diagram, element.ValueOf("component")) is { Length: > 0 } evolving
                ? new SetWardleyEvolveCommand(bodyPath, evolving, Present: false, 0d)
                : null,
            "PipelineComponent" when element.Parent is { } pipeline && IdOf(diagram, pipeline.ValueOf("component")) is { Length: > 0 } parent =>
                new SetWardleyPipelineMembershipCommand(bodyPath, parent, Text(element, "name"), Member: false),
            _ => null,
        };
    }

    /// <summary>
    /// The command that gives <paramref name="element"/> the decorators <paramref name="now"/>: a row's commit sets the whole
    /// set; an operation adds or removes the one decorator it toggles, and one that changes several at once has none.
    /// </summary>
    private static ICommand? Decorated(string bodyPath, DislElement element, List<WardleyDecorator> now, bool row)
    {
        if (row) return new SetWardleyDecoratorsCommand(bodyPath, element.Id, now);
        var before = Decorators(element.ValueOf("decorators"));
        return WardleyDecorators.All.Where(candidate => before.Contains(candidate) != now.Contains(candidate)).ToList() is [var toggled]
            ? new SetWardleyDecoratorCommand(bodyPath, element.Id, toggled, now.Contains(toggled))
            : null;
    }

    /// <summary>The five decorators the words <paramref name="value"/> holds name, in the order the format documents them; any other word is none.</summary>
    private static List<WardleyDecorator> Decorators(object? value)
    {
        var words = value is IEnumerable<object?> list ? list.OfType<string>().ToList() : [];
        return [.. WardleyDecorators.All.Where(candidate => words.Any(word => string.Equals(word, WardleyDecorators.Spell(candidate), StringComparison.OrdinalIgnoreCase)))];
    }

    private static WardleyLinkKind KindOf(DislElement link) => link.IsA("Flow") ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency;

    /// <summary>The id of the element a reference names, as an element or by its id; empty for none.</summary>
    private static string IdOf(DislDiagram diagram, object? reference) => reference switch
    {
        DislElement element => element.Id,
        string id when diagram.ElementById(id) is not null => id,
        _ => "",
    };

    private static string Text(IReadOnlyDictionary<string, object?> attributes, string name) =>
        attributes.GetValueOrDefault(name) is { } value ? value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    private static string Text(DislElement element, string name) => element.ValueOf(name) as string ?? "";

    private static double Number(IReadOnlyDictionary<string, object?> attributes, string name) => attributes.GetValueOrDefault(name) switch
    {
        double number => number,
        long whole => whole,
        _ => 0d,
    };

    private static double Number(DislElement element, string name) => element.ValueOf(name) switch
    {
        double number => number,
        long whole => whole,
        _ => 0d,
    };
}
