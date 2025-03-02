namespace EtAlii.Adp;

public class TagRemoveCommand : Command
{
    public required TagGroupIdentifier TagGroupId { get; init; }
    public required TagIdentifier OldTagId { get; init; }
    public required string OldTagName { get; init; }
}