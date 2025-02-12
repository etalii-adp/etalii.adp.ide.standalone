namespace EtAlii.Adp;

public class Diagram
{
    public required DiagramIdentifier Id { get; init; }
    public required User Owner { get; set; } = null!;
    public required string Name { get; set; } = string.Empty;
    public required string Description { get; init; }
    public required DiagramPosition Position { get; init; }
    public required DateTime CreationDate { get; init; }
    public required DateTime ModificationDate { get; set; }
    public required float Zoom { get; init; }
}