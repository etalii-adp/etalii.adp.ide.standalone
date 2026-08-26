namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The DSL's five component decorators, written in parentheses after the coordinates -
/// `component Payment [0.7, 0.72] (buy)` (Requirement 6.3).
/// </summary>
/// <remarks>
/// All five together, because the real parser carries them as five independent booleans in one
/// `decorators` object. Splitting them into "kinds" and "decorations" would draw a line the
/// format does not, and would narrow what a document can say.
/// <para>
/// `inertia` is deliberately absent: it is written the same way but the parser models it as a
/// separate boolean on the component, not a decorator (Requirement 6.2).
/// </para>
/// </remarks>
public enum WardleyDecorator
{
    /// <summary>A market rather than a single supplier.</summary>
    Market,

    /// <summary>An ecosystem of components around this one.</summary>
    Ecosystem,

    /// <summary>Build it.</summary>
    Build,

    /// <summary>Buy it.</summary>
    Buy,

    /// <summary>Outsource it.</summary>
    Outsource,
}
