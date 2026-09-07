using EtAlii.Adp.Common;
namespace EtAlii.Adp.Context;

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

    /// <summary>
    /// The project's root folder, captured when the action started. <c>SubmitInteraction</c>
    /// carries only an interaction id, so this is what lets an absolute path the commit
    /// produced be reported back to the client as a project-relative one.
    /// </summary>
    public required string RootPath { get; init; }

    public uint LastRevision { get; set; }
}
