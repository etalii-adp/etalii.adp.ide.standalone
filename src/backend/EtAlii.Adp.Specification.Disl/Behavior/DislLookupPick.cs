namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The element a person picked in the host's lookup (DISL 0.12 §9.4): its link as FBL 17.1 writes it, and the name it is shown by, which
/// the element a <c>lookup</c> action creates caches.
/// </summary>
/// <param name="Link">The picked element's link, <c>$&lt;document-id&gt;@&lt;document-name&gt;#&lt;element-id&gt;.&lt;name-prop&gt;=&lt;name&gt;</c>.</param>
/// <param name="Name">The picked element's name.</param>
public sealed record DislLookupPick(string Link, string Name)
{
    /// <summary>Why a <c>lookup</c> action is refused when the host offers no lookup.</summary>
    public const string NoneOffered = "Adding an element of another document needs the workspace's lookup, which this window does not offer yet.";
}
