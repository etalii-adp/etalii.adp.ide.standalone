using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The command of today's that writes what a transaction of the definition says (DISL §9.3): a create, a set of one
/// attribute, a connect or a remove, each onto the record whose handler splices the map's own lines. It stands in for
/// FBL's splices until the map is read and written through a binding (gap 1 of section C of wardley-map-disl-fbl), and
/// goes in task 11 with <see cref="WardleyDisl"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every element is named by its model id</b>, which is the store's identity for it (<see cref="WardleyDisl"/>):
/// a statement that names an element by its name, a link's end, an evolution's or a pipeline's <c>component</c>, is
/// named by its first holder's.
/// </para>
/// <para>
/// <b>A transaction no command writes has none</b>, and is refused by the caller in a sentence naming the operation
/// (<see cref="Unwritable"/>); <c>WardleyDerivedOperations.Tests</c> runs every operation of the definition over the
/// corpus so that none ships that way.
/// </para>
/// </remarks>
internal static class WardleyTransactionCommands
{
    /// <summary>The refusal of a transaction of <paramref name="operation"/> that no command of this map writes.</summary>
    public static string Unwritable(string operation) =>
        $"The definition's {operation} makes a change this map's commands cannot write.";

    /// <summary>
    /// The command that writes <paramref name="transaction"/> into the map at <paramref name="bodyPath"/>, whose model
    /// before it ran is <paramref name="diagram"/>; null when no command writes it.
    /// </summary>
    public static ICommand? Of(string bodyPath, DislDiagram diagram, DislTransaction transaction)
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
            [DislChange.Set set] => Set(bodyPath, diagram, set),
            [DislChange.Set first, ..] when transaction.Changes.All(change => change is DislChange.Set set && set.ElementId == first.ElementId) =>
                Set(bodyPath, diagram, Merged(first, transaction.Changes.Cast<DislChange.Set>())),
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
                    WardleyDecorator?[] decorators = [.. WardleyDecorators.All.Where(decorator => attributes.GetValueOrDefault(WardleyDecorators.Spell(decorator)) is true).Select(decorator => (WardleyDecorator?)decorator)];
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
                    return WardleyFunctions.Positions(Text(attributes, "positions")) is [List<object?> { Count: 2 } pair] && pair[0] is double visibility && pair[1] is double maturity
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

    private static ICommand? Set(string bodyPath, DislDiagram diagram, DislChange.Set set)
    {
        if (diagram.ElementById(set.ElementId) is not { } element) return null;
        var attributes = set.Attributes;

        if (element.IsA("Positioned"))
        {
            var decorators = WardleyDecorators.All.Where(decorator => attributes.ContainsKey(WardleyDecorators.Spell(decorator))).ToList();
            if (decorators.Count == WardleyDecorators.All.Count && attributes.Count == decorators.Count)
            {
                return new SetWardleyDecoratorsCommand(bodyPath, element.Id, [.. decorators.Where(decorator => attributes[WardleyDecorators.Spell(decorator)] is true)]);
            }

            if (attributes.Count != 1) return null;
            (string name, object? value) = attributes.Single();
            if (decorators is [var decorator]) return value is bool present ? new SetWardleyDecoratorCommand(bodyPath, element.Id, decorator, present) : null;
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
            foreach (var (name, value) in set.Attributes) attributes[name] = value;
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
