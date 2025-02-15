namespace EtAlii.Adp;

public class LinkAddChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier NewLinkId { get; init; }
    public required NodeIdentifier StartNodeId { get; init; }
    public required NodeIdentifier EndNodeId { get; init; }
    
    public static Change Apply(Diagram diagram, LinkIdentifier newLinkId, NodeIdentifier startNodeId, NodeIdentifier endNodeId)
    {
        return new LinkAddChange
        {
            DiagramId = diagram.Id, 
            NewLinkId = newLinkId,
            StartNodeId = startNodeId,
            EndNodeId = endNodeId
        };
    }
}