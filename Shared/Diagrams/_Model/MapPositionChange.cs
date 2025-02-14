namespace EtAlii.Adp;

public class MapPositionChange : Change
{
    public required DiagramPosition Position { get; init; }
    
    public static Change Create(Diagram diagram) => new MapPositionChange { Position = diagram.Position };
}