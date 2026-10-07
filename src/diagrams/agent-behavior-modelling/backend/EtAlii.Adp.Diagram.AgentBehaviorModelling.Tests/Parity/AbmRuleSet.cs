namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The findings as the module wrote them by hand before they were derived from the DISL definition
/// (runtime plan step S19b), kept verbatim as the oracle the derived ones are compared with (decision D6).
/// </summary>
internal static class AbmRuleSet
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

            if (node is { Kind: AbmNodeKinds.Retry, RetryCount: < 1 })
            {
                breaches.Add(new AbmBreach(AbmRuleIds.NoAttempts, "A Retry that allows no attempt never runs its child.", node.Line, IsError: true));
            }
        }

        return breaches;
    }
}
