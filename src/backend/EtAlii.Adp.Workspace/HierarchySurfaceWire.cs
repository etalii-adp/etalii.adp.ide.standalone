using EtAlii.Adp.Designer;
using Proto = EtAlii.Adp.Designer.Wire;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The hierarchy surface's read model and gestures to and from their wire form. One place, so the model a session
/// speaks and the messages a client reads cannot drift apart field by field.
/// </summary>
internal static class HierarchySurfaceWire
{
    public static Proto.HierarchySurfaceModel ToProto(HierarchySurfaceModel model)
    {
        var message = new Proto.HierarchySurfaceModel { Title = model.Title, ReadOnlyReason = model.ReadOnlyReason, Cause = model.Cause };
        message.Nodes.AddRange(model.Nodes.Select(ToProto));
        message.OptionLists.AddRange(model.OptionLists.Select(ToProto));
        message.Filters.AddRange(model.Filters.Select(filter => new Proto.HierarchySurfaceFilter { Id = filter.Id, Name = filter.Name, Query = filter.Query }));
        message.Notes.AddRange(model.Notes.Select(ToProto));
        message.Findings.AddRange(model.Findings.Select(finding => new Proto.HierarchySurfaceFinding
        {
            Code = finding.Code,
            Severity = finding.Severity,
            Message = finding.Message,
            NodeId = finding.NodeId,
            Line = finding.Line,
        }));
        message.Widths.AddRange(model.Widths.Select(column => new Proto.HierarchySurfaceColumnWidth { Id = column.Id, Level = column.Level, Width = column.Width }));
        return message;
    }

    public static Proto.HierarchySurfaceEditOutcome ToProto(HierarchySurfaceEditOutcome outcome) =>
        new() { EditId = outcome.EditId, Written = outcome.Written, Error = outcome.Error };

    public static HierarchySurfaceGesture FromProto(Proto.HierarchySurfaceGestureRequest request) =>
        new(request.Operation, request.TargetId, new Dictionary<string, string>(request.Params, StringComparer.Ordinal));

    private static Proto.HierarchySurfaceNode ToProto(HierarchySurfaceNode node)
    {
        var message = new Proto.HierarchySurfaceNode
        {
            Id = node.Id,
            Number = node.Number,
            Role = node.Role,
            Type = node.Type,
            ParentId = node.ParentId,
            Title = node.Title,
            Description = node.Description,
            Collapsed = node.Collapsed,
        };
        message.Labels.AddRange(node.Labels.Select(label =>
        {
            var values = new Proto.HierarchySurfaceLabelValues { Label = label.Label };
            values.Values.AddRange(label.Values);
            return values;
        }));
        message.References.AddRange(node.References.Select(ToProto));
        return message;
    }

    private static Proto.HierarchySurfaceOptionList ToProto(HierarchySurfaceOptionList list)
    {
        var message = new Proto.HierarchySurfaceOptionList { Label = list.Label };
        message.Options.AddRange(list.Options.Select(option => new Proto.HierarchySurfaceOption
        {
            Id = option.Id,
            Name = option.Name,
            Colour = option.Colour,
            Implicit = option.Implicit,
        }));
        return message;
    }

    private static Proto.HierarchySurfaceNote ToProto(HierarchySurfaceNote note)
    {
        var message = new Proto.HierarchySurfaceNote
        {
            Id = note.Id,
            Kind = note.Kind,
            About = note.About,
            AboutId = note.AboutId,
            Text = note.Text,
            Detail = note.Detail,
            Resolved = note.Resolved,
        };
        message.References.AddRange(note.References.Select(ToProto));
        return message;
    }

    private static Proto.HierarchySurfaceReference ToProto(HierarchySurfaceReference reference) =>
        new() { Start = reference.Start, Length = reference.Length, TargetId = reference.TargetId };
}
