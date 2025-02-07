using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class ElementPosition
{
    [JsonConverter(typeof(Vector2Converter))]
    public required Vector2 Coordinates { get; init; }

    public static implicit operator ElementPosition(Vector2 v) => new() { Coordinates = v };
    public static explicit operator Vector2(ElementPosition p) => p.Coordinates;
    
    public override string ToString() => $"(Element: {Coordinates.X}, {Coordinates.Y})";

}