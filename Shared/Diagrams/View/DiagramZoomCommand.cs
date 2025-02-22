namespace EtAlii.Adp;

public class DiagramZoomCommand : Command
{
    public required DiagramIdentifier Id { get; init; }
    public required double NewZoom { get; init; }
    public required double OldZoom { get; init; }
}