namespace EtAlii.Adp;

public class Link
{
    public required LinkIdentifier Id { get; init; }
    public required Node Start { get; init; }
    public required Node End { get; init; }
        
}