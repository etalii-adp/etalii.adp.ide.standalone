namespace EtAlii.Adp;

public class LinkAddChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required LinkIdentifier NewLinkId { get; init; }
    public required NodeIdentifier StartNodeId { get; init; }
    public required string StartPort { get; init; }
    public required NodeIdentifier EndNodeId { get; init; }
    public required string EndPort { get; init; }
    
    public static Change Apply(Diagram diagram, 
        LinkIdentifier newLinkId, 
        NodeIdentifier startNodeId, 
        string startPort,
        NodeIdentifier endNodeId,
        string endPort)
    {
        return new LinkAddChange
        {
            DiagramId = diagram.Id, 
            NewLinkId = newLinkId,
            StartNodeId = startNodeId,
            StartPort = startPort,
            EndNodeId = endNodeId,
            EndPort = endPort,
        };
    }
}