namespace EtAlii.Adp;

public class LinkAddCommand : Command
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier NewLinkId { get; init; }
    public required NodeIdentifier SourceNodeId { get; init; }
    public required string SourcePort { get; init; }
    public required NodeIdentifier TargetNodeId { get; init; }
    public required string TargetPort { get; init; }
}