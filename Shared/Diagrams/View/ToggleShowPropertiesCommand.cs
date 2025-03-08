namespace EtAlii.Adp;

public class ToggleShowPropertiesCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required bool NewShowProperties { get; init; }
    public required bool OldShowProperties { get; init; }
}