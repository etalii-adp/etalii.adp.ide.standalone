namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>How a link is drawn, which is what the arrow between two names means.</summary>
public enum WardleyLinkKind
{
    /// <summary><c>A-&gt;B</c>: an ordinary dependency.</summary>
    Dependency,

    /// <summary><c>A+&gt;B</c>: a flow link, drawn to show movement rather than need.</summary>
    Flow,
}
