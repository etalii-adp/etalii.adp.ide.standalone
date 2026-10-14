using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// One feedback loop the document claims: an identifier, a name, and the cycle of variables it
/// runs through.
/// </summary>
/// <remarks>
/// <para>
/// <b>A loop has no position.</b> It is drawn where its members are - at their centroid - so it
/// joins links in the category the viewport filters structurally rather than by coordinates
/// (Requirement 7.3).
/// </para>
/// <para>
/// <b>The identifier is what the author claims, not what the arrows say.</b> Whether this loop
/// is really reinforcing is arithmetic over its links, which the definition states (the derived
/// <c>computed</c> and <c>disagrees</c> of a <c>Loop</c>) and is kept beside the claim rather than
/// in place of it: the author's label may be the intent and the arrows the mistake, and a model
/// that overwrote one with the other could not express the disagreement (Requirement 3.3).
/// </para>
/// </remarks>
/// <param name="Identifier">As written - <c>R1</c>, <c>B2</c>. A claim about polarity, not a finding.</param>
/// <param name="Name">The descriptive name drawn beside the identifier.</param>
/// <param name="Variables">The cycle, in the order the document states it.</param>
/// <param name="Lines">Where the declaration sits.</param>
public sealed record CausalLoopLoop(
    string Identifier,
    string Name,
    IReadOnlyList<string> Variables,
    LineRange Lines)
{
    /// <summary>What the arrows say the loop is: the definition's derived <c>computed</c>.</summary>
    public LoopPolarityResult Computed { get; init; }

    /// <summary>Whether the identifier's claim and the arrows disagree: the definition's derived <c>disagrees</c>. Undecidable is no disagreement.</summary>
    public bool Disagrees { get; init; }

    /// <summary>The stable identity a selection and a delta address it by.</summary>
    public string Id => $"loop:{Identifier}";

    /// <summary>
    /// What the identifier claims: reinforcing where it starts with R, balancing where B.
    /// Null where the document wrote something this reading does not recognise, which is itself
    /// worth reporting rather than guessing at.
    /// </summary>
    public bool? ClaimsReinforcing => Identifier.Length == 0
        ? null
        : char.ToUpperInvariant(Identifier[0]) switch
        {
            'R' => true,
            'B' => false,
            _ => null,
        };
}
