namespace EtAlii.Adp;

public class NodeAddCommand : Command
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required NodeIdentifier NewNodeId { get; init; }
    public required NodePosition NewPosition { get; init; }
    public required string NewName { get; init; }
}