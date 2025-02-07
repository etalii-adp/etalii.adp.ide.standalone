namespace EtAlii.Adp;

public class Diagram
{
    public required DiagramIdentifier Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required DiagramPosition Position { get; init; }
    public required DateTime CreationDate { get; init; }
    public required DateTime ModificationDate { get; init; }
    public required float Zoom { get; init; }
}