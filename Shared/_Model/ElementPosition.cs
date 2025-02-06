using System.Diagnostics;
using System.Numerics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public struct ElementPosition
{
    public Vector2 Coordinates { get; set; }

    private ElementPosition(Vector2 coordinates)
    {
        Coordinates = coordinates;
    }

    public static implicit operator ElementPosition(Vector2 v) => new(v);
    public static explicit operator Vector2(ElementPosition p) => p.Coordinates;
    
    public override string ToString() => $"(Element: {Coordinates.X}, {Coordinates.Y})";

}