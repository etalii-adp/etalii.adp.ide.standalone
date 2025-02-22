namespace EtAlii.Adp;

public class NodeRemoveCommand : Command
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required NodeIdentifier OldNodeId { get; init; }
    public required NodePosition OldNodePosition { get; init; }
    public required string OldNodeName { get; init; } 
}