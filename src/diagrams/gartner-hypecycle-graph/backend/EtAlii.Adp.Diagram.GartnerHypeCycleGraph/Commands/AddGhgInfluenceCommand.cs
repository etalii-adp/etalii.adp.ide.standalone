using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Draws an influence from <paramref name="FromId"/> to <paramref name="ToId"/>.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="FromId">The influencing trend or trigger.</param>
/// <param name="FromEnd">
/// Where it leaves; null for the default, the source's last visible phase, bottom, 0.5. Ignored for a
/// trigger, which has no phases: its end is <see cref="GhgEnd.None"/>.
/// </param>
/// <param name="ToId">The influenced trend.</param>
/// <param name="ToEnd">Where it arrives; null for the default, the target's Peak, top, 0.5.</param>
/// <param name="InfluenceId">Empty to have one minted, once, as for <see cref="AddGhgTrendCommand"/>.</param>
/// <remarks>
/// Refused on the same checks the canvas makes - a self-influence, a second influence in the same
/// direction, hidden or not, and one ending at a trigger (Requirement 6.4, 7.3, 3.2) - because the
/// request may not come from a canvas that checked.
/// </remarks>
public sealed record AddGhgInfluenceCommand(
    string BodyPath,
    string FromId,
    GhgEnd? FromEnd,
    string ToId,
    GhgEnd? ToEnd,
    string InfluenceId = "") : ICommand;
