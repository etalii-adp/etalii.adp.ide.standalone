using EtAlii.Adp.Backend.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Turns a project, its graph and its layout into the core element and delta vocabulary
/// (Requirement 7.1) - without extending that vocabulary by a single field.
/// </summary>
/// <remarks>
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
    /// one-hop partners, and every edge whose two ends are both delivered.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(
        AnsibleProject project,
        AnsibleGraph graph,
        DiagramViewport viewport,
        IReadOnlyDictionary<string, RegistrationPosition>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(graph);

        var boxes = Arranged(AnsibleLayout.Compute(graph, _metrics), stored);

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes.Where(node => boxes.TryGetValue(node.Id, out var box) && Intersects(box, viewport)))
        {
            delivered.Add(node.Id);
        }

        // One hop, exactly: an edge with one end in view brings the other end with it, so the
        // connector always has two boxes to be drawn between.
        foreach (var edge in graph.Edges)
        {
            if (delivered.Contains(edge.SourceId) && edge.TargetId.Length > 0)
            {
                delivered.Add(edge.TargetId);
            }
            else if (edge.TargetId.Length > 0 && delivered.Contains(edge.TargetId))
            {
                delivered.Add(edge.SourceId);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var node in graph.Nodes.Where(node => delivered.Contains(node.Id)))
        {
            elements.Add(NodeElement(project, node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var edge in graph.Edges)
        {
            // An edge with an unresolved target still draws - from its source, to nothing, which
            // is how a reader sees that something is missing rather than seeing nothing at all.
            var sourceDelivered = delivered.Contains(edge.SourceId);
            var targetDelivered = edge.TargetId.Length == 0 || delivered.Contains(edge.TargetId);
            if (sourceDelivered && targetDelivered)
            {
                elements.Add(EdgeElement(edge, boxes.GetValueOrDefault(edge.SourceId)));
            }
        }

        return elements;
    }

    /// <summary>
    /// What to send a connection whose diagram was <paramref name="previous"/> and is now
    /// <paramref name="current"/>: the ids that went away, then everything that is there now.
    /// </summary>
    /// <remarks>
    /// Add is an upsert, so re-sending an unchanged element is correct but wasteful; only ids
    /// that genuinely disappeared need removing first. Nothing here is an edit delta, because
    /// nothing here is edited - the folder changed and this is what it says now.
    /// </remarks>
    public IReadOnlyList<DiagramDelta> Diff(IReadOnlyList<DiagramElement> previous, IReadOnlyList<DiagramElement> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var deltas = new List<DiagramDelta>();

        var live = current.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = previous.Select(element => element.Id).Where(id => !live.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        if (current.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(current));
        }

        return deltas;
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
    private static IReadOnlyDictionary<string, AnsibleBox> Arranged(
        IReadOnlyDictionary<string, AnsibleBox> computed,
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

    private DiagramElement NodeElement(AnsibleProject project, AnsibleNode node, AnsibleBox box)
    {
        var payload = new Wire.AnsibleElementPayload
        {
            Name = node.Name,
            Kind = KindOf(node.Kind),
            PlayIndex = node.PlayIndex,
            Width = box.Width,
            Height = box.Height,
        };
        payload.ProjectRelativePath.AddRange(Segments(node.RelativePath));

        switch (node.Kind)
        {
            case AnsibleNodeKind.Playbook:
            case AnsibleNodeKind.Play:
                payload.Hosts = HostsOf(project, node);
                // The project's annotations ride the entry playbook, which is where a reader
                // looking for "what is this project" will be looking (Requirement 4.1).
                payload.Annotations.AddRange(project.Annotations);
                break;

            case AnsibleNodeKind.Role when project.Role(node.Name) is { } role:
                payload.Contents = ContentsOf(role.Contents);
                payload.Hollow = role.Contents.IsHollow;
                break;

            case AnsibleNodeKind.Inventory when Inventory(project, node) is { } inventory:
                payload.Groups.AddRange(inventory.Groups.Select(group =>
                    new Wire.AnsibleInventoryGroup { Name = group.Name, HostCount = group.HostCount }));
                break;

            case AnsibleNodeKind.VariableFolder when VariableFolder(project, node) is { } folder:
                payload.FileCount = folder.FileCount;
                break;
        }

        return new DiagramElement(node.Id, box.X, box.Y, TypeOf(node.Kind), PayloadTypeUrl, payload.ToByteArray());
    }

    private static DiagramElement EdgeElement(AnsibleEdge edge, AnsibleBox anchor)
    {
        var payload = new Wire.AnsibleElementPayload
        {
            Name = edge.Directive.Target,
            Kind = Wire.AnsibleElementKind.Edge,
            Unresolvable = edge.Resolution == AnsibleTargetResolution.Unresolvable,
            Edge = new Wire.AnsibleEdge
            {
                SourceId = edge.SourceId,
                TargetId = edge.TargetId,
                Kind = EdgeKindOf(edge.Kind),
                Dynamic = edge.IsDynamic,
                Directive = DirectiveNameOf(edge.Directive.Kind),
                TargetAsWritten = edge.Directive.Target,
                Condition = edge.Directive.Condition,
                DeclaredAtLine = edge.Directive.Line,
                Missing = edge.Resolution == AnsibleTargetResolution.Missing,
            },
        };
        payload.Edge.DeclaredIn.AddRange(Segments(edge.Directive.DeclaredIn));

        // An edge is positioned at its source; the canvas routes it between the two boxes.
        return new DiagramElement(edge.Id, anchor.X, anchor.Y, EdgeType, PayloadTypeUrl, payload.ToByteArray());
    }

    private static string HostsOf(AnsibleProject project, AnsibleNode node)
    {
        var playbook = project.Playbooks.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        if (playbook is null)
        {
            return "";
        }

        if (node.Kind == AnsibleNodeKind.Play)
        {
            var hash = node.Id.LastIndexOf('#');
            return hash >= 0 && int.TryParse(node.Id[(hash + 1)..], out var index)
                ? playbook.Plays.FirstOrDefault(play => play.Index == index)?.Hosts ?? ""
                : "";
        }

        // A single-play playbook carries its play's pattern, since no play box is drawn for it.
        return playbook.Plays.Count == 1 ? playbook.Plays[0].Hosts : "";
    }

    private static AnsibleInventory? Inventory(AnsibleProject project, AnsibleNode node) =>
        project.Inventories.FirstOrDefault(inventory =>
            string.Equals(inventory.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static AnsibleVariableFolder? VariableFolder(AnsibleProject project, AnsibleNode node) =>
        project.Inventories
            .SelectMany(inventory => inventory.VariableFolders)
            .FirstOrDefault(folder => string.Equals(folder.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static Wire.AnsibleRoleContents ContentsOf(AnsibleRoleContents contents) => new()
    {
        TaskFiles = contents.TaskFiles,
        Handlers = contents.Handlers,
        Templates = contents.Templates,
        Files = contents.Files,
        Defaults = contents.Defaults,
        Vars = contents.Vars,
        HasMeta = contents.HasMeta,
        HasLibrary = contents.HasLibrary,
    };

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool Intersects(AnsibleBox box, DiagramViewport viewport) =>
        box.X <= viewport.MaxX && box.Right >= viewport.MinX &&
        box.Y <= viewport.MaxY && box.Bottom >= viewport.MinY;

    private static string TypeOf(AnsibleNodeKind kind) => kind switch
    {
        AnsibleNodeKind.Playbook => PlaybookType,
        AnsibleNodeKind.Play => PlayType,
        AnsibleNodeKind.Role => RoleType,
        AnsibleNodeKind.TaskFile => TaskFileType,
        AnsibleNodeKind.Inventory => InventoryType,
        AnsibleNodeKind.VariableFolder => VariableFolderType,
        _ => EdgeType,
    };

    private static Wire.AnsibleElementKind KindOf(AnsibleNodeKind kind) => kind switch
    {
        AnsibleNodeKind.Playbook => Wire.AnsibleElementKind.Playbook,
        AnsibleNodeKind.Play => Wire.AnsibleElementKind.Play,
        AnsibleNodeKind.Role => Wire.AnsibleElementKind.Role,
        AnsibleNodeKind.TaskFile => Wire.AnsibleElementKind.TaskFile,
        AnsibleNodeKind.Inventory => Wire.AnsibleElementKind.Inventory,
        AnsibleNodeKind.VariableFolder => Wire.AnsibleElementKind.VariableFolder,
        _ => Wire.AnsibleElementKind.Unspecified,
    };

    private static Wire.AnsibleEdgeKind EdgeKindOf(AnsibleEdgeKind kind) => kind switch
    {
        AnsibleEdgeKind.UsesRole => Wire.AnsibleEdgeKind.UsesRole,
        AnsibleEdgeKind.ImportsPlaybook => Wire.AnsibleEdgeKind.ImportsPlaybook,
        AnsibleEdgeKind.IncludesTasks => Wire.AnsibleEdgeKind.IncludesTasks,
        AnsibleEdgeKind.DependsOn => Wire.AnsibleEdgeKind.DependsOn,
        AnsibleEdgeKind.Targets => Wire.AnsibleEdgeKind.Targets,
        _ => Wire.AnsibleEdgeKind.Unspecified,
    };

    /// <summary>The directive as the file spells it, which is what the property grid shows.</summary>
    private static string DirectiveNameOf(AnsibleDirectiveKind kind) => kind switch
    {
        AnsibleDirectiveKind.Roles => "roles:",
        AnsibleDirectiveKind.ImportRole => "import_role",
        AnsibleDirectiveKind.IncludeRole => "include_role",
        AnsibleDirectiveKind.ImportPlaybook => "import_playbook",
        AnsibleDirectiveKind.ImportTasks => "import_tasks",
        AnsibleDirectiveKind.IncludeTasks => "include_tasks",
        AnsibleDirectiveKind.Dependency => "dependencies",
        _ => "",
    };
}
