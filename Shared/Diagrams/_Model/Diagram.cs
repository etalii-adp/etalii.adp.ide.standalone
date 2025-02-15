namespace EtAlii.Adp;

public class Diagram
{
    public required DiagramIdentifier Id { get; init; }
    public required User Owner { get; set; } = null!;
    public required string Name { get; set; } = string.Empty;
    public required string Description { get; init; }
    public required DiagramPosition Position { get; set; }
    public required DateTime CreationDate { get; init; }
    public required DateTime ModificationDate { get; set; }
    
    /// <summary>
    /// Default zoom is 1f.
    /// </summary>
    public required double Zoom { get; set; } = 1f; 
}