using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Turns the definition's model of a folder and its layout into the core element and delta
/// vocabulary (Requirement 7.1) - without extending that vocabulary by a single field.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reads the DISL model and nothing else</b> (ansible-structure-disl-fbl, Requirement 3.5):
/// every fact a payload carries is an attribute the plugin delivers and the definition declares, the
/// directive's words are the definition's labels, and nodes and relations go out in the model's
/// order, which is the plugin's (finding B6). The <c>.proto</c> payloads are the host's wire and do
/// not change.
/// </para>
/// <para>
/// <b>Two of the four delta kinds are never emitted, and that is the design rather than an
/// omission.</b> Nothing on this diagram folds, so no group or ungroup delta exists; nothing on
/// it is edited, so an element only ever appears, disappears, or is replaced wholesale because
/// the folder changed. A push is <c>Remove</c> then <c>Add</c> for what actually differs.
/// </para>
/// <para>
/// <b>The viewport rule is the mindmap's, for the mindmap's reason.</b> Deliver what the
/// viewport intersects, plus one hop of graph partners: the canvas draws an edge from the boxes
/// at its two ends, so an element whose partner was culled would lose the line running off the
/// edge of the screen towards it. One hop exactly - following partners of partners would walk
/// the whole graph and deliver the entire project, which is what a viewport exists to avoid.
/// </para>
/// </remarks>
public sealed class AnsibleElementMapper
{
    /// <summary>The mime-style element kinds this type puts on the wire (Requirement 7.1).</summary>
    public const string PlaybookType = "ansible/structure+playbook";
    public const string PlayType = "ansible/structure+play";
    public const string RoleType = "ansible/structure+role";
    public const string TaskFileType = "ansible/structure+taskfile";
    public const string InventoryType = "ansible/structure+inventory";
    public const string VariableFolderType = "ansible/structure+vars";
    public const string EdgeType = "ansible/structure+edge";

    private static readonly string PayloadTypeUrl =
        $"type.googleapis.com/{Wire.AnsibleElementPayload.Descriptor.FullName}";

    private readonly AnsibleMetrics _metrics;

    public AnsibleElementMapper(AnsibleMetrics? metrics = null) => _metrics = metrics ?? AnsibleMetrics.Default;

    /// <summary>
    /// Every element a connection with this viewport should see: the nodes it intersects, their
    /// one-hop partners, and every relation whose two ends are both delivered.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(
        DislDiagram diagram,
        DiagramViewport viewport,
        IReadOnlyDictionary<string, RegistrationPosition>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        var boxes = Arranged(AnsibleLayout.Compute(diagram, _metrics), stored);

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in diagram.Nodes.Where(node => boxes.TryGetValue(node.Id, out var box) && viewport.Intersects(box)))
        {
            delivered.Add(node.Id);
        }

        // One hop, exactly: a relation with one end in view brings the other end with it, so the
        // connector always has two boxes to be drawn between.
        foreach (var relation in diagram.Relations)
        {
            var sourceId = SourceIdOf(relation);
            var targetId = TargetIdOf(relation);
            if (delivered.Contains(sourceId) && targetId.Length > 0)
            {
                delivered.Add(targetId);
            }
            else if (targetId.Length > 0 && delivered.Contains(targetId))
            {
                delivered.Add(sourceId);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var node in diagram.Nodes.Where(node => delivered.Contains(node.Id)))
        {
            elements.Add(NodeElement(node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var relation in diagram.Relations)
        {
            // A relation with no target still draws - from its source, to nothing, which is how a
            // reader sees that something is missing rather than seeing nothing at all.
            var sourceId = SourceIdOf(relation);
            var targetId = TargetIdOf(relation);
            var sourceDelivered = delivered.Contains(sourceId);
            var targetDelivered = targetId.Length == 0 || delivered.Contains(targetId);
            if (sourceDelivered && targetDelivered)
            {
                elements.Add(EdgeElement(relation, sourceId, targetId, boxes.GetValueOrDefault(sourceId)));
            }
        }

        return elements;
    }

    /// <summary>
    /// The computed layout with authored positions overlaid: a node the registration's
    /// <c>layout:</c> block names sits where the user put it, every other node stays where the
    /// layout engine put it (Requirement 1.4).
    /// </summary>
    /// <remarks>
    /// Only the origin moves; a box keeps the size the backend computed, because the client
    /// draws the box it is given rather than re-measuring. A stored id the folder no longer
    /// produces - a play removed, a file renamed - is dropped here by
    /// <see cref="RegistrationLayout.Apply"/> rather than being drawn at a phantom position
    /// (Requirement 3.1).
    /// </remarks>
    private static IReadOnlyDictionary<string, Rect> Arranged(
        IReadOnlyDictionary<string, Rect> computed,
        IReadOnlyDictionary<string, RegistrationPosition>? stored)
    {
        if (stored is not { Count: > 0 })
        {
            return computed;
        }

        var positions = RegistrationLayout.Apply(
            computed.ToDictionary(entry => entry.Key, entry => new RegistrationPosition(entry.Value.X, entry.Value.Y), StringComparer.Ordinal),
            stored);

        return computed.ToDictionary(
            entry => entry.Key,
            entry => positions.TryGetValue(entry.Key, out var position)
                ? entry.Value with { X = position.X, Y = position.Y }
                : entry.Value,
            StringComparer.Ordinal);
    }

    private static DiagramElement NodeElement(DislElement node, Rect box)
    {
        var type = node.Type.Name;
        var payload = new Wire.AnsibleElementPayload
        {
            Name = Text(node, "name"),
            Kind = KindOf(type),
            PlayIndex = type == "Play" ? (int)Number(node, "playIndex") : -1,
            Width = box.Width,
            Height = box.Height,
        };
        payload.ProjectRelativePath.AddRange(Segments(Text(node, "path")));

        switch (type)
        {
            case "Playbook":
            case "Play":
                payload.Hosts = Text(node, "hosts");
                // The project's annotations ride the entry playbook, which is where a reader
                // looking for "what is this project" will be looking (Requirement 4.1).
                payload.Annotations.AddRange(Items(node, "annotations").OfType<string>());
                break;

            // A role, an inventory and a variable folder carry what the plugin read of them; one
            // it found nothing for carries nothing, as the payload of a role it did not find did.
            case "Role" when node.Attributes.ContainsKey("contents") && node.ValueOf("contents") is CelMap contents:
                payload.Contents = ContentsOf(contents);
                payload.Hollow = node.ValueOf("hollow") is true;
                break;

            case "Inventory" when node.Attributes.ContainsKey("groups"):
                payload.Groups.AddRange(Items(node, "groups").OfType<CelMap>().Select(group =>
                    new Wire.AnsibleInventoryGroup { Name = group.GetValueOrDefault("name") as string ?? "", HostCount = (int)Count(group, "hostCount") }));
                break;

            case "VariableFolder" when node.Attributes.ContainsKey("fileCount"):
                payload.FileCount = (int)Number(node, "fileCount");
                break;
        }

        return new DiagramElement(node.Id, box.X, box.Y, TypeOf(type), PayloadTypeUrl, payload.ToByteArray());
    }

    private static DiagramElement EdgeElement(DislElement relation, string sourceId, string targetId, Rect anchor)
    {
        var resolution = Text(relation, "resolution");
        var target = Text(relation, "targetAsWritten");
        var payload = new Wire.AnsibleElementPayload
        {
            Name = target,
            Kind = Wire.AnsibleElementKind.Edge,
            Unresolvable = resolution == "expression",
            Edge = new Wire.AnsibleEdge
            {
                SourceId = sourceId,
                TargetId = targetId,
                Kind = EdgeKindOf(relation.Type.Name),
                Dynamic = relation.ValueOf("dynamic") is true,
                Directive = AnsibleDefinition.Label(relation, "directive"),
                TargetAsWritten = target,
                Condition = Text(relation, "condition"),
                DeclaredAtLine = (uint)Number(relation, "declaredAtLine"),
                Missing = resolution == "missing",
            },
        };
        payload.Edge.DeclaredIn.AddRange(Segments(Text(relation, "declaredIn")));

        // An edge is positioned at its source; the canvas routes it between the two boxes.
        return new DiagramElement(relation.Id, anchor.X, anchor.Y, EdgeType, PayloadTypeUrl, payload.ToByteArray());
    }

    /// <summary>The id of the node a relation is declared by.</summary>
    private static string SourceIdOf(DislElement relation) => relation.Source?.Id ?? relation.SourceId ?? "";

    /// <summary>The id of the node a relation reaches; empty for one that is missing or an expression, which reaches none.</summary>
    private static string TargetIdOf(DislElement relation) => relation.Target?.Id ?? "";

    private static Wire.AnsibleRoleContents ContentsOf(CelMap contents) => new()
    {
        TaskFiles = (int)Count(contents, "taskFiles"),
        Handlers = (int)Count(contents, "handlers"),
        Templates = (int)Count(contents, "templates"),
        Files = (int)Count(contents, "files"),
        Defaults = (int)Count(contents, "defaults"),
        Vars = (int)Count(contents, "vars"),
        HasMeta = contents.GetValueOrDefault("hasMeta") is true,
        HasLibrary = contents.GetValueOrDefault("hasLibrary") is true,
    };

    private static string Text(DislElement element, string attribute) => element.ValueOf(attribute) as string ?? "";

    private static long Number(DislElement element, string attribute) => element.ValueOf(attribute) is long number ? number : 0;

    private static IReadOnlyList<object?> Items(DislElement element, string attribute) =>
        element.ValueOf(attribute) as IReadOnlyList<object?> ?? [];

    private static long Count(CelMap item, string field) => item.GetValueOrDefault(field) is long count ? count : 0;

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static string TypeOf(string type) => type switch
    {
        "Playbook" => PlaybookType,
        "Play" => PlayType,
        "Role" => RoleType,
        "TaskFile" => TaskFileType,
        "Inventory" => InventoryType,
        "VariableFolder" => VariableFolderType,
        _ => EdgeType,
    };

    private static Wire.AnsibleElementKind KindOf(string type) => type switch
    {
        "Playbook" => Wire.AnsibleElementKind.Playbook,
        "Play" => Wire.AnsibleElementKind.Play,
        "Role" => Wire.AnsibleElementKind.Role,
        "TaskFile" => Wire.AnsibleElementKind.TaskFile,
        "Inventory" => Wire.AnsibleElementKind.Inventory,
        "VariableFolder" => Wire.AnsibleElementKind.VariableFolder,
        _ => Wire.AnsibleElementKind.Unspecified,
    };

    private static Wire.AnsibleEdgeKind EdgeKindOf(string type) => type switch
    {
        "UsesRole" => Wire.AnsibleEdgeKind.UsesRole,
        "ImportsPlaybook" => Wire.AnsibleEdgeKind.ImportsPlaybook,
        "IncludesTasks" => Wire.AnsibleEdgeKind.IncludesTasks,
        "DependsOn" => Wire.AnsibleEdgeKind.DependsOn,
        "Targets" => Wire.AnsibleEdgeKind.Targets,
        _ => Wire.AnsibleEdgeKind.Unspecified,
    };
}
