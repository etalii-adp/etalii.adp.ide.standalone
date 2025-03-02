namespace EtAlii.Adp;

public class TagGroupRenameCommand : Command
{
    public required TagGroupIdentifier TagGroupId { get; init; }
     
    public required string NewName { get; init; }
    public required string OldName { get; init; }
}