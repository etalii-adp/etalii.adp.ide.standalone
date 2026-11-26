namespace EtAlii.Adp.Context;

/// <summary>
/// A lookup asking the user to pick an element of the workspace by name as they type (DISL 0.12 §9.4, etalii.adp spec 092):
/// described as data, and searched through <paramref name="Search"/>, so the context service knows nothing of what it searches.
/// </summary>
/// <param name="Search">The candidates for the text typed so far: <see cref="Shown"/> of them, and one more where more match, which tells the lookup that what is typed narrows them.</param>
public sealed record ContextLookupRequest(
    string Title,
    string Icon,
    string Placeholder,
    string EmptyMessage,
    Func<string, IReadOnlyList<ContextLookupCandidate>> Search)
{
    /// <summary>How many candidates a lookup shows at most.</summary>
    public const int Shown = 50;
}
