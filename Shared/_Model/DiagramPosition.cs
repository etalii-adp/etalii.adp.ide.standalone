using System.Numerics;

namespace EtAlii.Adp;

public  struct DiagramPosition
{
    public Vector2 Coordinates { get; set; }

    public DiagramPosition(Vector2 coordinates)
    {
        Coordinates = coordinates;
    }
        
    public DiagramPosition(DiagramPosition position)
    {
        Coordinates = position.Coordinates;
    }

}