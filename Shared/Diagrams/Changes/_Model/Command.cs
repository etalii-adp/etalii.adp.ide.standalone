using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(DiagramPositionCommand), typeDiscriminator: nameof(DiagramPositionCommand))]
[JsonDerivedType(typeof(DiagramZoomCommand), typeDiscriminator: nameof(DiagramZoomCommand))]
[JsonDerivedType(typeof(NodeAddCommand), typeDiscriminator: nameof(NodeAddCommand))]
[JsonDerivedType(typeof(NodeRemoveCommand), typeDiscriminator: nameof(NodeRemoveCommand))]
[JsonDerivedType(typeof(NodeMoveCommand), typeDiscriminator: nameof(NodeMoveCommand))]
[JsonDerivedType(typeof(NodeRenameCommand), typeDiscriminator: nameof(NodeRenameCommand))]
[JsonDerivedType(typeof(LinkAddCommand), typeDiscriminator: nameof(LinkAddCommand))]
[JsonDerivedType(typeof(LinkRemoveCommand), typeDiscriminator: nameof(LinkRemoveCommand))]
[JsonDerivedType(typeof(TagGroupAddCommand), typeDiscriminator: nameof(TagGroupAddCommand))]
[JsonDerivedType(typeof(TagGroupRemoveCommand), typeDiscriminator: nameof(TagGroupRemoveCommand))]
[JsonDerivedType(typeof(TagGroupRenameCommand), typeDiscriminator: nameof(TagGroupRenameCommand))]
public abstract class Command
{
    public bool Undo { get; set; }
}