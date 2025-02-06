namespace EtAlii.Adp;

public class Diagram
{
    public DiagramIdentifier Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public DiagramPosition Position { get; set; }
    public DateTime CreationDate { get; set; }
    public DateTime ModificationDate { get; set; }
    public float Zoom { get; set; }
}