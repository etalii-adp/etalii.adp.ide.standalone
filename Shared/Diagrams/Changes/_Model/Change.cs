using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(DiagramPositionChange), typeDiscriminator: nameof(DiagramPositionChange))]
[JsonDerivedType(typeof(DiagramZoomChange), typeDiscriminator: nameof(DiagramZoomChange))]
[JsonDerivedType(typeof(NodeAddChange), typeDiscriminator: nameof(NodeAddChange))]
[JsonDerivedType(typeof(NodeRemoveChange), typeDiscriminator: nameof(NodeRemoveChange))]
[JsonDerivedType(typeof(NodeMoveChange), typeDiscriminator: nameof(NodeMoveChange))]
[JsonDerivedType(typeof(NodeRenameChange), typeDiscriminator: nameof(NodeRenameChange))]
[JsonDerivedType(typeof(LinkAddChange), typeDiscriminator: nameof(LinkAddChange))]
[JsonDerivedType(typeof(LinkRemoveChange), typeDiscriminator: nameof(LinkRemoveChange))]
public abstract class Change
{
    public bool Undo { get; set; }
}