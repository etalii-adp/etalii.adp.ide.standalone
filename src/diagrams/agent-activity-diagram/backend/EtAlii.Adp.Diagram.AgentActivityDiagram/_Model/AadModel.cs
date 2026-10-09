using System.Globalization;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>The five kinds of element an agent activity diagram draws.</summary>
public enum AadKind
{
    Project,
    Specification,
    Agent,
    Location,
    Environment,
}

/// <summary>One row of a list inside an element: a task or a pull request.</summary>
/// <param name="Id">The row's id, as the file stores it or as the reader gave it.</param>
/// <param name="Status">A task's status as the definition's member name; empty for a pull request and for a status the definition does not have.</param>
/// <param name="Updated">When the row was last changed, as the file holds it; empty when it does not say.</param>
public sealed record AadRow(string Id, string Title, string Status, string Updated, string Link);

/// <summary>One element, with everything the file says about it.</summary>
/// <param name="Name">The element's name; for a location, its branch.</param>
/// <param name="Status">A specification's status as the definition's member name; empty otherwise.</param>
/// <param name="StatusLabel">That status in words, or the file's own word for a status the definition does not have.</param>
/// <param name="Folder">A location's folder: the worktree's, or <c>Default</c>.</param>
/// <param name="KindLabel">An environment's kind, in words.</param>
/// <param name="Rows">A specification's tasks, or a location's pull requests.</param>
public sealed record AadElement(
    AadKind Kind,
    string Id,
    string Name,
    string Status,
    string StatusLabel,
    string Link,
    string Folder,
    string BranchLink,
    string FolderLink,
    string KindLabel,
    IReadOnlyList<AadRow> Rows);

/// <summary>One relation, read from a key on the element at its "one" end.</summary>
/// <param name="Id">The relation's derived id, such as <c>specification:a-dev2</c>: stable while the element holding the key keeps its id.</param>
/// <param name="Type">The relation type's name in the definition.</param>
public sealed record AadRelation(string Id, string Type, string From, string To);

/// <summary>Where the reader put an element.</summary>
public readonly record struct AadPlacement(double X, double Y);

/// <summary>
/// An activity file as the diagram reads it: its elements and relations, and the reader's own part
/// - what is locked where, which groups are folded, and whether archived specifications show.
/// </summary>
public sealed record AadModel(
    IReadOnlyList<AadElement> Elements,
    IReadOnlyList<AadRelation> Relations,
    IReadOnlyDictionary<string, AadPlacement> Placements,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Collapsed,
    bool ShowArchived)
{
    /// <summary>The model of a file that holds nothing.</summary>
    private static AadModel Empty { get; } = new([], [], new Dictionary<string, AadPlacement>(), new Dictionary<string, IReadOnlySet<string>>(), false);

    /// <summary>
    /// Reads the model off an open body. What the body cannot say - an unreadable file - reads as
    /// empty; the findings say why, and the session keeps showing what it last read.
    /// </summary>
    public static AadModel Read(AadBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var disl = body.Disl;
        if (disl.IsUnreadable)
        {
            return Empty;
        }

        var diagram = disl.Diagram;
        List<AadElement> elements =
        [
            .. diagram.NodesOfType("Project").Select(node => Element(AadKind.Project, node)),
            .. diagram.NodesOfType("Specification").Select(node => Element(AadKind.Specification, node)),
            .. diagram.NodesOfType("Agent").Select(node => Element(AadKind.Agent, node)),
            .. diagram.NodesOfType("Location").Select(node => Element(AadKind.Location, node)),
            .. diagram.NodesOfType("Environment").Select(node => Element(AadKind.Environment, node)),
        ];
        List<AadRelation> relations =
        [
            .. diagram.Relations
                .Where(relation => relation is { SourceId.Length: > 0, TargetId.Length: > 0 })
                .Select(relation => new AadRelation(relation.Id, relation.Type.Name, relation.SourceId!, relation.TargetId!)),
        ];

        var reading = body.Model;
        var placements = new Dictionary<string, AadPlacement>(StringComparer.Ordinal);
        foreach (var placement in reading.Elements.Where(entry => entry.Type == "Placement"))
        {
            // The first entry for an element applies; a second is a duplicate the findings report.
            if (Text(placement, "element") is { Length: > 0 } id && Number(placement, "x") is { } x && Number(placement, "y") is { } y)
            {
                placements.TryAdd(id, new AadPlacement(x, y));
            }
        }

        var stored = new Dictionary<(string Element, string Group), bool>();
        foreach (var state in reading.Elements.Where(entry => entry.Type == "GroupState"))
        {
            if (Text(state, "element") is { Length: > 0 } id && Text(state, "group") is { Length: > 0 } group)
            {
                stored.TryAdd((id, GroupKey(group)), state.Attributes.GetValueOrDefault("collapsed") is true);
            }
        }

        var collapsed = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var element in elements.Where(element => element.Kind is AadKind.Specification or AadKind.Location))
        {
            IEnumerable<string> groups = element.Kind == AadKind.Specification
                ? AadDefinition.TaskStatus.Members.Select(member => member.Name)
                : [AadDefinition.PullRequestsGroup];
            collapsed[element.Id] = groups
                .Where(group => stored.TryGetValue((element.Id, group), out var folded) ? folded : AadDefinition.CollapsedByDefault.Contains(group))
                .ToHashSet(StringComparer.Ordinal);
        }

        var showArchived = reading.Elements.FirstOrDefault(entry => entry.Type == "Diagram")?.Attributes.GetValueOrDefault("showArchived") is true;
        return new AadModel(elements, relations, placements, collapsed, showArchived);
    }

    /// <summary>A group's key on the wire: a task status by its member name, however the file wrote it.</summary>
    private static string GroupKey(string written) => AadDefinition.TaskStatus.Find(written)?.Name ?? written;

    private static AadElement Element(AadKind kind, DislElement node)
    {
        var status = kind == AadKind.Specification ? Text(node, "status") : "";
        var known = AadDefinition.SpecificationStatus.Find(status);
        var environmentKind = kind == AadKind.Environment ? AadDefinition.EnvironmentKind.Find(Text(node, "environmentKind")) : null;
        return new AadElement(
            kind,
            node.Id,
            kind == AadKind.Location ? Text(node, "branch") : Text(node, "name"),
            known?.Name ?? "",
            known?.Label ?? status,
            Text(node, "link"),
            kind == AadKind.Location ? Text(node, "folder") is { Length: > 0 } folder ? folder : "Default" : "",
            Text(node, "branchLink"),
            Text(node, "folderLink"),
            environmentKind?.Label ?? (kind == AadKind.Environment ? Text(node, "environmentKind") : ""),
            [.. node.Children.Where(child => child.IsA("Task") || child.IsA("PullRequest")).Select(Row)]);
    }

    private static AadRow Row(DislElement child) => new(
        child.Id,
        Text(child, "title"),
        child.IsA("Task") ? AadDefinition.TaskStatus.Find(Text(child, "status"))?.Name ?? "" : "",
        Text(child, "updated"),
        Text(child, "link"));

    private static string Text(DislElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) ? AsText(value) : "";

    private static string? Text(FblElement entry, string attribute) =>
        entry.Attributes.TryGetValue(attribute, out var value) ? AsText(value) : null;

    private static string AsText(object? value) => value switch
    {
        null => "",
        string text => text,
        DislElement referenced => referenced.Id,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static double? Number(FblElement entry, string attribute) =>
        entry.Attributes.GetValueOrDefault(attribute) switch
        {
            double number => number,
            long number => number,
            int number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
}
