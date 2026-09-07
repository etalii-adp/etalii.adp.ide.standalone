using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The mistakes a dependency graph document can hide, as a pure function over the model - no
/// file, no canvas, no connection.
/// </summary>
/// <remarks>
/// Everything here is a <b>warning</b> naming its element, because the rest of the diagram still
/// draws. The one error this type has - a document that is not YAML - never reaches these rules;
/// the validator reports it as a single located problem instead of running graph rules over a
/// model that is empty only because the parse failed.
/// </remarks>
public static class DependencyGraphRuleSet
{
    /// <summary>The problems in <paramref name="model"/>, or none.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var problems = new List<DiagramProblem>();
        JudgeIdentity(model, problems);
        JudgeRelations(model, problems);
        return problems;
    }

    private static void JudgeIdentity(DependencyGraphModel model, List<DiagramProblem> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, name, range) in Declarations(model))
        {
            if (id.Length == 0)
            {
                problems.Add(Warn(
                    $"{name} has no id, so nothing can select, connect or edit it.",
                    DependencyGraphRules.MissingId,
                    range));
                continue;
            }

            if (!seen.Add(id))
            {
                problems.Add(Warn(
                    $"The id '{id}' is declared more than once, which makes every reference to it ambiguous.",
                    DependencyGraphRules.DuplicateId,
                    range));
            }
        }
    }

    private static void JudgeRelations(DependencyGraphModel model, List<DiagramProblem> problems)
    {
        var ids = model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var relation in model.Relations)
        {
            foreach (var end in new[] { relation.From, relation.To })
            {
                if (end.Length > 0 && !ids.Contains(end))
                {
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Warning,
                        $"A dependency names '{end}', and no node with that id is in this graph.",
                        DependencyGraphRules.DanglingRelation,
                        new DiagramProblemElementLocation(relation.Id)));
                }
            }

            if (relation.From.Length > 0 && string.Equals(relation.From, relation.To, StringComparison.Ordinal))
            {
                // Nothing in the application can create this - connecting an element to itself is
                // refused at the command - so a file that says it was hand-edited.
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{relation.From}' is declared to depend on itself, which says nothing about what needs what.",
                    DependencyGraphRules.SelfDependency,
                    new DiagramProblemElementLocation(relation.Id)));
            }
        }
    }

    private static IEnumerable<(string Id, string Name, LineRange Range)> Declarations(DependencyGraphModel model)
    {
        foreach (var element in model.Elements)
        {
            yield return (element.Id, NameOf(element), element.Range);
        }

        foreach (var relation in model.Relations)
        {
            yield return (relation.Id, "A dependency", relation.Range);
        }
    }

    private static string NameOf(DependencyGraphElement element) =>
        element.Label.Length > 0 ? $"'{element.Label}'" : $"'{element.Id}'";

    private static DiagramProblem Warn(string message, string ruleId, LineRange range) =>
        new(DiagramProblemSeverity.Warning, message, ruleId, new DiagramProblemLineLocation((uint)(range.Start + 1)));
}
