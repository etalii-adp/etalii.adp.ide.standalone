namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// One in-flight action awaiting the user's answer, owned by the connection that
/// started it. <see cref="LastRevision"/> is the highest proposal revision seen so
/// far, so a verdict for text the user has already moved past can be recognised.
/// </summary>
public sealed class ContextInteraction
{
    public required ShortGuid Id { get; init; }

    public required ShortGuid WatchId { get; init; }

    public required ContextTarget Target { get; init; }

    public required string ActionId { get; init; }

    public required IContextActionProvider Provider { get; init; }

    public uint LastRevision { get; set; }
}
