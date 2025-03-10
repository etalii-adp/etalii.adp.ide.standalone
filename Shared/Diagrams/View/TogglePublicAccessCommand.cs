namespace EtAlii.Adp;

public class TogglePublicAccessCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required bool NewAllowPublicAccess { get; init; }
    public required bool OldAllowPublicAccess { get; init; }
}