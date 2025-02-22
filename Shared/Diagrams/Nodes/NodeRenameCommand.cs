namespace EtAlii.Adp;

public class NodeRenameCommand : Command
{
    public required NodeIdentifier NodeId { get; init; }
     
    public required string NewName { get; init; }
    public required string OldName { get; init; }
}