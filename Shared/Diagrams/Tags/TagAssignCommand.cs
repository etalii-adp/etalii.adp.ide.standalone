namespace EtAlii.Adp;

public class TagAssignCommand : Command
{
    public required TagGroupIdentifier TagGroupId { get; init; }
    public required TagIdentifier TagId { get; init; }
}