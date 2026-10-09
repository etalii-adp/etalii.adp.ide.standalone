using Google.Protobuf;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// Turns an activity file's model into what the canvas draws: one element per project,
/// specification, agent, location and environment, one per relation, and one that carries what is
/// set for the diagram as a whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is culled by the viewport.</b> Only a locked element has a position here; the others
/// are placed by the canvas's layout, which needs every element to place any of them. The diagram
/// is a team's work at one moment - hundreds of elements, not tens of thousands.
/// </para>
/// <para>
/// <b>An archived specification is left out unless the switch is on</b>, with the relations at
/// either end of it and nothing else (Requirement 4.7): its agents, if it still has any, stay.
/// </para>
/// </remarks>
public sealed class AadElementMapper
{
    private const string Prefix = "etalii/agent-activity-diagram+";

    public const string ProjectType = Prefix + "project";
    public const string SpecificationType = Prefix + "specification";
    public const string AgentType = Prefix + "agent";
    public const string LocationType = Prefix + "location";
    public const string EnvironmentType = Prefix + "environment";
    public const string RelationType = Prefix + "relation";
    public const string ViewType = Prefix + "view";

    /// <summary>The id of the element that carries the diagram's own settings. No file entry can have it: an id cannot hold a space.</summary>
    public const string ViewId = "diagram view";

    /// <summary>What a location's second line, its folder, adds to the height the definition gives it.</summary>
    private const double LocationFolderLine = 8;

    /// <summary>The member name of the status that hides a specification.</summary>
    private const string Archived = "archived";

    public IReadOnlyList<DiagramElement> Visible(AadModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);
        _ = viewport;

        var shown = model.Elements
            .Where(element => model.ShowArchived || element.Kind != AadKind.Specification || element.Status != Archived)
            .ToList();
        var shownIds = shown.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);

        return
        [
            Pack(ViewId, 0, 0, ViewType, new AadViewPayload { ShowArchived = model.ShowArchived }),
            .. shown.Select(element => Element(element, model)),
            .. model.Relations
                .Where(relation => shownIds.Contains(relation.From) && shownIds.Contains(relation.To))
                .Select(relation => Pack(relation.Id, 0, 0, RelationType, new AadRelationPayload { FromElementId = relation.From, ToElementId = relation.To })),
        ];
    }

    private static DiagramElement Element(AadElement element, AadModel model)
    {
        var pinned = model.Placements.TryGetValue(element.Id, out var placement);
        (string type, string declared) = element.Kind switch
        {
            AadKind.Project => (ProjectType, "Project"),
            AadKind.Specification => (SpecificationType, "Specification"),
            AadKind.Agent => (AgentType, "Agent"),
            AadKind.Location => (LocationType, "Location"),
            _ => (EnvironmentType, "Environment"),
        };

        // The size is the definition's, so that every host draws an element as large. A location
        // is the one exception, and only in height: this canvas writes the folder as a second line
        // under the branch where the definition lists it, and that line needs its eight units.
        (double width, double height) = AadDefinition.SizeOf(declared);
        if (element.Kind == AadKind.Location)
        {
            height += LocationFolderLine;
        }
        var payload = new AadElementPayload
        {
            Name = element.Name,
            Status = element.Status,
            StatusLabel = element.StatusLabel,
            Link = element.Link,
            Folder = element.Folder,
            BranchLink = element.BranchLink,
            FolderLink = element.FolderLink,
            KindLabel = element.KindLabel,
            Pinned = pinned,
            Width = width,
            Height = height,
        };
        if (model.Collapsed.TryGetValue(element.Id, out var collapsed))
        {
            // In the definition's order, so two renderings of one model are byte for byte the same
            // and the change detection sees a fold as a change and nothing else as one.
            payload.Collapsed.AddRange(collapsed.Order(StringComparer.Ordinal));
        }

        var rows = element.Rows.Select(row => new AadRowPayload { Id = row.Id, Title = row.Title, Status = row.Status, Updated = row.Updated, Link = row.Link });
        if (element.Kind == AadKind.Specification)
        {
            payload.Tasks.AddRange(rows);
        }
        else if (element.Kind == AadKind.Location)
        {
            payload.PullRequests.AddRange(rows);
        }

        return Pack(element.Id, pinned ? placement.X : 0, pinned ? placement.Y : 0, type, payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
