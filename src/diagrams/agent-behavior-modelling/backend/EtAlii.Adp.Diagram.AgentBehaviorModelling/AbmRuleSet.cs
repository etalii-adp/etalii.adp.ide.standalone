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

/// <summary>What the notation forbids, read over the whole model.</summary>
/// <remarks>
/// <b>A document that breaks a rule still opens and still draws</b>: the agent reading the file
/// meets it as written, so the diagram shows it as written and the Errors and Warnings panel names
/// what an agent would trip over.
/// </remarks>
public static class AbmRuleSet
{
    /// <summary>Every breach in <paramref name="model"/>, the parser's own problems first.</summary>
    public static IReadOnlyList<AbmBreach> Breaches(AbmModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var breaches = model.Problems.Select(problem => new AbmBreach(problem.RuleId, problem.Message, problem.Line, IsError: false)).ToList();

        if (model.SectionLine is null)
        {
            breaches.Add(new AbmBreach(AbmRuleIds.NoBehavior, "This file has no Behavior heading, so there is no behavior tree to draw. Add a node to start one.", 0, IsError: false, IsInformation: true));
            return breaches;
        }

        if (model.Nodes.Count == 0)
        {
            breaches.Add(new AbmBreach(AbmRuleIds.NoBehavior, "The Behavior section holds no list yet. Add a node to start the tree.", model.SectionLine.Value, IsError: false, IsInformation: true));
            return breaches;
        }

        var roots = model.Roots;
        if (roots.Count > 1)
        {
            breaches.Add(new AbmBreach(
                AbmRuleIds.SeveralRoots,
                $"The tree has {roots.Count} roots, and an agent starts at one. Put them under a Do in order or a Try in order to say how they relate.",
                roots[1].Line,
                IsError: false));
        }

        foreach (var node in model.Nodes)
        {
            switch (node.Category)
            {
                case AbmNodeCategory.Leaf when node.ChildIds.Count > 0:
                    breaches.Add(new AbmBreach(
                        AbmRuleIds.LeafWithChildren,
                        $"\"{node.Keyword}: {node.Label}\" holds no children, but has {node.ChildIds.Count}. Make it a Do in order, or move them out.",
                        node.Line,
                        IsError: true));
                    break;
                case AbmNodeCategory.Decorator when node.ChildIds.Count != 1:
                    breaches.Add(new AbmBreach(
                        AbmRuleIds.DecoratorChildren,
                        $"\"{node.Keyword}\" wraps exactly one child, but has {node.ChildIds.Count}." + (node.ChildIds.Count > 1 ? " Put them under a Do in order first." : " Add the node it applies to beneath it."),
                        node.Line,
                        IsError: true));
                    break;
                case AbmNodeCategory.Composite when node.ChildIds.Count == 0:
                    breaches.Add(new AbmBreach(
                        AbmRuleIds.EmptyComposite,
                        $"\"{node.Keyword}: {node.Label}\" has no children, so it has nothing to run.",
                        node.Line,
                        IsError: false));
                    break;
            }

            if (node.Kind == AbmNodeKinds.Retry && node.RetryCount < 1)
            {
                breaches.Add(new AbmBreach(AbmRuleIds.NoAttempts, "A Retry that allows no attempt never runs its child.", node.Line, IsError: true));
            }
        }

        return breaches;
    }
}
