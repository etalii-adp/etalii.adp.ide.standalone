namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>The ids of everything the validator reports, one per rule.</summary>
public static class AbmRuleIds
{
    /// <summary>An item without a keyword, read as a Do.</summary>
    public const string NoKeyword = "abm.no-keyword";

    /// <summary>A Do, Check, Ask the user or Delegate with children.</summary>
    public const string LeafWithChildren = "abm.leaf-with-children";

    /// <summary>A Retry, Repeat until, Only while or Ask approval before without exactly one child.</summary>
    public const string DecoratorChildren = "abm.decorator-children";

    /// <summary>A Do in order, Try in order or Do together with no children.</summary>
    public const string EmptyComposite = "abm.empty-composite";

    /// <summary>More than one root.</summary>
    public const string SeveralRoots = "abm.several-roots";

    /// <summary>A Retry that allows no attempt.</summary>
    public const string NoAttempts = "abm.no-attempts";

    /// <summary>A file with no Behavior section, or one with no list in it.</summary>
    public const string NoBehavior = "abm.no-behavior";
}

/// <summary>Something wrong with a behavior model, with how serious it is.</summary>
/// <param name="RuleId">One of <see cref="AbmRuleIds"/>.</param>
/// <param name="Message">A sentence a person can act on.</param>
/// <param name="Line">The zero-based line it is about.</param>
/// <param name="IsError">Whether the tree says something an agent cannot follow, rather than something merely odd.</param>
/// <param name="IsInformation">Whether it is worth saying while nothing is wrong.</param>
public sealed record AbmBreach(string RuleId, string Message, int Line, bool IsError, bool IsInformation = false);
