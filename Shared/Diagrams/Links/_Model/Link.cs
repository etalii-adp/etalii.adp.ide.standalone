namespace EtAlii.Adp;

public class Link
{
    public required LinkIdentifier Id { get; init; }
    public Diagram Diagram { get; set; } = null!;
    public required Node SourceNode { get; init; }
    public required string SourcePort { get; init; }
    public required Node TargetNode { get; init; }
    public required string TargetPort { get; init; }
}