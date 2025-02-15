namespace EtAlii.Adp;

public class MapPositionChange : Change
{
    public required DiagramIdentifier Id { get; init; }
    public required DiagramPosition NewPosition { get; init; }
    public required DiagramPosition OldPosition { get; init; }
    
    public static Change Apply(Diagram diagram, DiagramPosition newPosition)
    {
        var oldPosition = diagram.Position;
        diagram.Position = newPosition; 
        
        return new MapPositionChange 
        { 
            Id = diagram.Id, 
            NewPosition = newPosition,
            OldPosition = oldPosition,
        };
    }
}