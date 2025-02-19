namespace EtAlii.Adp;

public class LinkAddChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier NewLinkId { get; init; }
    public required NodeIdentifier SourceNodeId { get; init; }
    public required string SourcePort { get; init; }
    public required NodeIdentifier TargetNodeId { get; init; }
    public required string TargetPort { get; init; }
    
    public static Change Apply(Diagram diagram, 
        LinkIdentifier newLinkId, 
        NodeIdentifier sourceNodeId, 
        string sourcePort,
        NodeIdentifier targetNodeId,
        string targetPort)
    {
        return new LinkAddChange
        {
            DiagramId = diagram.Id, 
            NewLinkId = newLinkId,
            SourceNodeId = sourceNodeId,
            SourcePort = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPort = targetPort,
        };
    }
}