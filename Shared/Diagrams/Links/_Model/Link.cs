namespace EtAlii.Adp;

public class Link
{
    public required LinkIdentifier Id { get; init; }
    public Diagram Diagram { get; set; } = null!;
    public required Node StartNode { get; init; }
    public required string StartPort { get; init; }
    public required Node EndNode { get; init; }
    public required string EndPort { get; init; }
}