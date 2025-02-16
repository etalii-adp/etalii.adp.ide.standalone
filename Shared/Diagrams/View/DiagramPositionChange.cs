namespace EtAlii.Adp;

public class DiagramPositionChange : Change
{
    public required DiagramIdentifier Id { get; init; }
    public required DiagramPosition NewPosition { get; init; }
    public required DiagramPosition OldPosition { get; init; }
    
    public static Change Apply(Diagram diagram, DiagramPosition newPosition)
    {
        var oldPosition = diagram.Position;
        diagram.Position = newPosition; 
        
        return new DiagramPositionChange 
        { 
            Id = diagram.Id, 
            NewPosition = newPosition,
            OldPosition = oldPosition,
        };
    }
}