namespace EtAlii.Adp;

public class DiagramPositionCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required DiagramPosition NewPosition { get; init; }
    public required DiagramPosition OldPosition { get; init; }
}