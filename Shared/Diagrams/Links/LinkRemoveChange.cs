namespace EtAlii.Adp;

public class LinkRemoveChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier OldLinkId { get; init; }
    public required NodeIdentifier StartNodeId { get; init; }
    public required string StartPort { get; init; }
    public required NodeIdentifier EndNodeId { get; init; }
    public required string EndPort { get; init; }
    
    public static Change Apply(Diagram diagram, 
        LinkIdentifier oldLinkId, 
        NodeIdentifier startNodeId, 
        string startPort,
        NodeIdentifier endNodeId,
        string endPort)
    {
        return new LinkRemoveChange
        {
            DiagramId = diagram.Id, 
            OldLinkId = oldLinkId,
            StartNodeId = startNodeId,
            StartPort = startPort,
            EndNodeId = endNodeId,
            EndPort = endPort,
        };
    }
}