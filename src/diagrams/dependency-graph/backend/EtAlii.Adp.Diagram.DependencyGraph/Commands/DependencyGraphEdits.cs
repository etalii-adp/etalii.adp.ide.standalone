using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// What every handler does first: locate against <b>current</b> state, because undo and redo
/// dispatch the same command instance again later and the document has moved on since; and the
/// changes the handlers write, each as the binding plans it.
/// </summary>
internal static class DependencyGraphEdits
{
    /// <summary>The binding's type of a node.</summary>
    public const string Node = "Node";

    /// <summary>The binding's type of a dependency.</summary>
    public const string Dependency = "Dependency";

    /// <summary>The first node written with <paramref name="elementId"/> in the current model, or null.</summary>
    public static DislElement? NodeOf(DislModel model, string elementId) =>
        model.Diagram.NodesOfType("Node").FirstOrDefault(node => DependencyGraphDefinition.WrittenId(node) == elementId);

    /// <summary>The first dependency written with <paramref name="relationId"/> in the current model, or null.</summary>
    public static DislElement? RelationOf(DislModel model, string relationId) =>
        model.Diagram.RelationsOfType("DependsOn").FirstOrDefault(relation => DependencyGraphDefinition.WrittenId(relation) == relationId);

    /// <summary>A new node, as the definition's <c>Node</c> holds it.</summary>
    public static DislChange.Create Create(string id, string label, double x, int row) =>
        new("Node", id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = label, ["x"] = x, ["row"] = (long)row }, null, null);

    /// <summary>A new dependency: <paramref name="from"/> depends on <paramref name="to"/>, with <paramref name="label"/> when it has one.</summary>
    public static DislChange.Connect Connect(string id, string from, string to, string label) =>
        new("DependsOn", id, from, to, label.Length > 0
            ? new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = label }
            : new Dictionary<string, object?>(StringComparer.Ordinal));

    /// <summary>Sets attributes of the entry of <paramref name="type"/> written with <paramref name="id"/>; <paramref name="gone"/> when there is none.</summary>
    public static EditOutcome Set(DependencyGraphBody body, string type, string id, IReadOnlyDictionary<string, object?> attributes, string gone) =>
        body.Find(type, id) is { } entry ? body.Change(new ModelChange.Set(entry.Id, attributes)) : EditOutcome.Refused(gone);

    /// <summary>
    /// Removes the entry of <paramref name="type"/> written with <paramref name="id"/>, with what the binding's
    /// cascade takes with it; <paramref name="gone"/> when there is none.
    /// </summary>
    public static EditOutcome Remove(DependencyGraphBody body, string type, string id, string gone) =>
        body.Find(type, id) is { } entry ? body.Change(new ModelChange.Remove(entry.Id)) : EditOutcome.Refused(gone);

    /// <summary>One transaction of <paramref name="changes"/>, written in order.</summary>
    public static DislTransaction Transaction(params DislChange[] changes) => new(changes, [], null);
}
