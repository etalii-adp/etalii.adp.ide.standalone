using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(DiagramPositionChange), typeDiscriminator: nameof(DiagramPositionChange))]
[JsonDerivedType(typeof(DiagramZoomChange), typeDiscriminator: nameof(DiagramZoomChange))]
[JsonDerivedType(typeof(NodeAddChange), typeDiscriminator: nameof(NodeAddChange))]
[JsonDerivedType(typeof(NodeMoveChange), typeDiscriminator: nameof(NodeMoveChange))]
public abstract class Change
{
}