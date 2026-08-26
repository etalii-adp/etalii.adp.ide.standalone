namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A `url name [address]` definition (Requirement 6.6). Elements refer to it by name through
/// their own `url(name)`, so one address can serve several of them.
/// </summary>
/// <param name="Name">The name elements refer to it by.</param>
/// <param name="Address">The address itself.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <remarks>
/// The address is untrusted input read from a file someone else may have authored: it is
/// rendered as text and only ever opened as an external link the user explicitly activates,
/// never interpreted as markup (the spec's Security section).
/// </remarks>
public sealed record WardleyUrlDefinition(string Name, string Address, uint Line);
