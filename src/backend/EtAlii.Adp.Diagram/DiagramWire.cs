using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Maps a module's backend delta records to the contract's proto - the one place that mapping
/// lives. <see cref="DiagramService"/> writes what it returns onto the stream, and a test that
/// needs exactly the bytes a client receives calls it rather than repeating the mapping.
/// </summary>
public static class DiagramWire
{
    public static Delta ToProto(DiagramDelta delta) => delta switch
    {
        DiagramAddDelta add => new Delta { Add = new Add { Elements = { add.Elements.Select(ToProto) } } },
        DiagramRemoveDelta remove => new Delta { Remove = new Remove { ElementIds = { remove.ElementIds.Select(id => new ElementId { Value = id }) } } },
        DiagramGroupDelta group => new Delta { Group = new Group { SourceElementIds = { group.SourceElementIds.Select(id => new ElementId { Value = id }) }, GroupElement = ToProto(group.GroupElement) } },
        DiagramUngroupDelta ungroup => new Delta { Ungroup = new Ungroup { GroupElementId = new ElementId { Value = ungroup.GroupElementId }, Elements = { ungroup.Elements.Select(ToProto) } } },
        _ => throw new ArgumentOutOfRangeException(nameof(delta)),
    };

    public static Element ToProto(DiagramElement element) => new()
    {
        Id = new ElementId { Value = element.Id },
        Position = new Point2D { X = element.X, Y = element.Y },
        Type = element.Type,
        Payload = new Google.Protobuf.WellKnownTypes.Any
        {
            TypeUrl = element.PayloadTypeUrl,
            Value = ByteString.CopyFrom(element.Payload.Span),
        },
    };
}
