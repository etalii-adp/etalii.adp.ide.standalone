namespace EtAlii.Adp;

public class LinkRemoveChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier OldLinkId { get; init; }
    public required NodeIdentifier SourceNodeId { get; init; }
    public required string SourcePort { get; init; }
    public required NodeIdentifier TargetNodeId { get; init; }
    public required string TargetPort { get; init; }
    
    public static Change Apply(Diagram diagram, 
        LinkIdentifier oldLinkId, 
        NodeIdentifier sourceNodeId, 
        string sourcePort,
        NodeIdentifier targetNodeId,
        string targetPort)
    {
        return new LinkRemoveChange
        {
            DiagramId = diagram.Id, 
            OldLinkId = oldLinkId,
            SourceNodeId = sourceNodeId,
            SourcePort = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPort = targetPort,
        };
    }
}