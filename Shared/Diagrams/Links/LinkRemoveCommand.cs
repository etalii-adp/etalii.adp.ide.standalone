namespace EtAlii.Adp;

public class LinkRemoveCommand : Command
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier OldLinkId { get; init; }
    public required NodeIdentifier SourceNodeId { get; init; }
    public required string SourcePort { get; init; }
    public required NodeIdentifier TargetNodeId { get; init; }
    public required string TargetPort { get; init; }
}