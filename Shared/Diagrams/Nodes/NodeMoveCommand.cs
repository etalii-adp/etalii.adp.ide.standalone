namespace EtAlii.Adp;

public class NodeMoveCommand : Command
{
    public required NodeIdentifier NodeId { get; init; }
     
    public required NodePosition NewPosition { get; init; }
    public required NodePosition OldPosition { get; init; }
}