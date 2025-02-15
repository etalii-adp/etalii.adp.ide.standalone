using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(DiagramPositionChange), typeDiscriminator: nameof(DiagramPositionChange))]
[JsonDerivedType(typeof(DiagramZoomChange), typeDiscriminator: nameof(DiagramZoomChange))]
public abstract class Change
{
}