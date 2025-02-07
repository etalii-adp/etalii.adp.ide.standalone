using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class DiagramPosition
{
    [JsonConverter(typeof(Vector2Converter))]
    public required Vector2 Coordinates { get; init; }
    
    public static implicit operator DiagramPosition(Vector2 v) => new() { Coordinates = v };
    public static explicit operator Vector2(DiagramPosition p) => p.Coordinates;
    
    public override string ToString() => $"Position: {Coordinates.X}, {Coordinates.Y}";
}