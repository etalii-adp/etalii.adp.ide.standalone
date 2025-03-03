namespace EtAlii.Adp;

public class TagGroupRemoveCommand : Command
{
    public required NodeIdentifier NodeId { get; init; }
    public required TagGroupIdentifier OldTagGroupId { get; init; }
    public required string OldTagGroupName { get; init; }
    public required TagGroupMode OldTagGroupMode { get; init; }
}