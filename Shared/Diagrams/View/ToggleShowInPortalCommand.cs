namespace EtAlii.Adp;

public class ToggleShowInPortalCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required bool NewShowInPortal { get; init; }
    public required bool OldShowInPortal { get; init; }
}