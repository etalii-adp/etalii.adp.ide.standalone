namespace EtAlii.Adp;

public class TagAddCommand : Command
{
    public required TagGroupIdentifier TagGroupId { get; init; }
    public required TagIdentifier NewTagId { get; init; }
    public required string NewTagName { get; init; }
}