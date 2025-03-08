namespace EtAlii.Adp;

public class TagGroupAddCommand : Command
{
    public required NodeIdentifier NodeId { get; init; }
    public required TagGroupIdentifier NewTagGroupId { get; init; }
    public required string NewTagGroupName { get; init; }
    
    public required int NewTagGroupOrder { get; init; }
    
    public required TagGroupMode NewTagGroupMode { get; init; }
}