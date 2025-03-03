namespace EtAlii.Adp;

public class TagUnassignCommand : Command
{
    public required TagGroupIdentifier TagGroupId { get; init; }
    public required TagIdentifier TagId { get; init; }
}