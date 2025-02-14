using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(MapPositionChange), typeDiscriminator: nameof(MapPositionChange))]
[JsonDerivedType(typeof(MapZoomChange), typeDiscriminator: nameof(MapZoomChange))]
public abstract class Change
{
}