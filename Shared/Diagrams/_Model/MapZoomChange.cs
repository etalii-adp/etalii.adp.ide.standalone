namespace EtAlii.Adp;

public class MapZoomChange : Change
{
    public required float Zoom { get; init; }
    
    public static Change Create(Diagram diagram) => new MapZoomChange { Zoom = diagram.Zoom };
}