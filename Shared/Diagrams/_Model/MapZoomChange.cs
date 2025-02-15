namespace EtAlii.Adp;

public class MapZoomChange : Change
{
    public required DiagramIdentifier Id { get; init; }
    public required double NewZoom { get; init; }
    public required double OldZoom { get; init; }
    
    public static Change Apply(Diagram diagram, double newZoom)
    {
        var oldZoom = diagram.Zoom;
        diagram.Zoom = newZoom;
        
        return new MapZoomChange
        {
            Id = diagram.Id, 
            NewZoom = newZoom, 
            OldZoom = oldZoom
        };
    }
}