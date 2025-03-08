namespace EtAlii.Adp;

public class ToggleShowNavigationCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required bool NewShowNavigation { get; init; }
    public required bool OldShowNavigation { get; init; }
}