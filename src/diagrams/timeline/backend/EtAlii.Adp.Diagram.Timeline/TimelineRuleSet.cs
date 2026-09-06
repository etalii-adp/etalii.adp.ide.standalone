using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The mistakes a timeline document can hide, as a pure function over the model - no file, no
/// canvas, no connection (Requirement 12.1).
/// </summary>
/// <remarks>
/// Everything here is a <b>warning</b> naming its element, because Requirement 12.2 says the
/// rest of the diagram still draws. The one error this type has - a document that is not YAML -
/// never reaches these rules; the validator reports it as a single located problem instead of
/// running graph rules over a model that is empty only because the parse failed.
/// </remarks>
public static class TimelineRuleSet
{
    /// <summary>The problems in <paramref name="model"/>, or none.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var problems = new List<DiagramProblem>();
        JudgeIdentity(model, problems);
        JudgeTimes(model, problems);
        JudgeConnections(model, problems);
        return problems;
    }

    private static void JudgeIdentity(TimelineModel model, List<DiagramProblem> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, name, range) in Declarations(model))
        {
            if (id.Length == 0)
            {
                problems.Add(Warn(
                    $"{name} has no id, so nothing can select, connect or edit it.",
                    TimelineRules.MissingId,
                    range));
                continue;
            }

            if (!seen.Add(id))
            {
                problems.Add(Warn(
                    $"The id '{id}' is declared more than once, which makes every reference to it ambiguous.",
                    TimelineRules.DuplicateId,
                    range));
            }
        }
    }

    private static void JudgeTimes(TimelineModel model, List<DiagramProblem> problems)
    {
        foreach (var element in model.Elements)
        {
            var name = NameOf(element);

            if (!element.Begin.IsReadable)
            {
                problems.Add(Warn(
                    $"{name} begins at '{element.Begin.Text}', which is not a time this timeline can read.",
                    TimelineRules.UnreadableTime,
                    element));
            }

            if (element.End is { IsReadable: false })
            {
                problems.Add(Warn(
                    $"{name} ends at '{element.End.Text}', which is not a time this timeline can read.",
                    TimelineRules.UnreadableTime,
                    element));
            }

            if (element is { Begin.IsReadable: true, End.IsReadable: true })
            {
                if (element.End.Value < element.Begin.Value)
                {
                    problems.Add(Warn(
                        $"{name} ends before it begins. No edit in ADP can create this, so the file was changed by hand.",
                        TimelineRules.EndBeforeBegin,
                        element));
                }

                if (element.End.Precision != element.Begin.Precision)
                {
                    problems.Add(Warn(
                        $"{name} mixes a date-only value with a date-time one; begin and end should use the same form.",
                        TimelineRules.MixedPrecision,
                        element));
                }
            }
        }
    }

    private static void JudgeConnections(TimelineModel model, List<DiagramProblem> problems)
    {
        var ids = model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var connection in model.Connections)
        {
            foreach (var end in new[] { connection.From, connection.To })
            {
                if (end.Length > 0 && !ids.Contains(end))
                {
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Warning,
                        $"A relation names '{end}', and no element with that id is on this timeline.",
                        TimelineRules.DanglingConnection,
                        new DiagramProblemElementLocation(connection.Id)));
                }
            }
        }
    }

    private static IEnumerable<(string Id, string Name, LineRange Range)> Declarations(TimelineModel model)
    {
        foreach (var element in model.Elements)
        {
            yield return (element.Id, NameOf(element), element.Range);
        }

        foreach (var connection in model.Connections)
        {
            yield return (connection.Id, "A relation", connection.Range);
        }
    }

    private static string NameOf(TimelineElement element) =>
        element.Label.Length > 0 ? $"'{element.Label}'" : $"'{element.Id}'";

    private static DiagramProblem Warn(string message, string ruleId, TimelineElement element) =>
        new(DiagramProblemSeverity.Warning, message, ruleId, new DiagramProblemElementLocation(element.Id));

    private static DiagramProblem Warn(string message, string ruleId, LineRange range) =>
        new(DiagramProblemSeverity.Warning, message, ruleId, new DiagramProblemLineLocation((uint)(range.Start + 1)));
}
