using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>What a node may hold beneath it.</summary>
public enum AbmNodeCategory
{
    /// <summary>One or more children, run by a rule of its own: in order, as fallbacks, together.</summary>
    Composite,

    /// <summary>Exactly one child, whose run it wraps: retried, repeated, guarded, approved.</summary>
    Decorator,

    /// <summary>No children: the node is the work, the question or the hand-off itself.</summary>
    Leaf,
}

/// <summary>One kind of node: its document id, the keyword the Markdown carries, and what it may hold.</summary>
/// <param name="Id">The kind's id, as the wire type and the toolbox name it (<c>sequence</c>).</param>
/// <param name="Keyword">What the Markdown says in bold before the label (<c>Do in order</c>).</param>
/// <param name="Category">What the node may hold beneath it.</param>
/// <param name="Meaning">One sentence on what the node does, as the agent is told it.</param>
public sealed record AbmNodeKind(string Id, string Keyword, AbmNodeCategory Category, string Meaning);

/// <summary>
/// The eleven kinds of node an agent behavior model has: the game industry's behavior tree
/// vocabulary, tuned for an agent that works turn by turn with a person and with tools.
/// </summary>
/// <remarks>
/// <para>
/// <b>The keyword is plain English on purpose.</b> The file is read by a model that may never have
/// heard of a selector or a sequence; "Try in order" and "Do in order" tell it what to do. The ids
/// keep the behavior tree names, so a reader who knows the literature recognises every one.
/// </para>
/// <para>
/// <b>Four kinds are the agent-engineering additions.</b> <see cref="Approval"/> and
/// <see cref="Ask"/> make the person a first-class part of the tree rather than an exception
/// handler; <see cref="Delegate"/> is the sub-tree reference, aimed at a sub-agent or another
/// behavior file; <see cref="Repeat"/> is the agentic loop - work until a condition holds.
/// </para>
/// </remarks>
public static partial class AbmNodeKinds
{
    /// <summary>Run the children one after another; fail at the first that fails.</summary>
    public const string Sequence = "sequence";

    /// <summary>Try the children one after another; succeed at the first that succeeds.</summary>
    public const string Fallback = "fallback";

    /// <summary>The children are independent and may run at once; succeed when all succeed.</summary>
    public const string Parallel = "parallel";

    /// <summary>Run the child again when it fails, at most N attempts in all.</summary>
    public const string Retry = "retry";

    /// <summary>Run the child again and again until the label's condition holds.</summary>
    public const string Repeat = "repeat";

    /// <summary>Run the child only while the label's condition holds.</summary>
    public const string Guard = "guard";

    /// <summary>Say what the child will do and wait for the user's approval first.</summary>
    public const string Approval = "approval";

    /// <summary>A question answered from what the agent already knows; never acts.</summary>
    public const string Check = "check";

    /// <summary>One piece of work: a tool call, an answer, an edit.</summary>
    public const string Action = "action";

    /// <summary>Ask the user, and wait for the answer.</summary>
    public const string Ask = "ask";

    /// <summary>Hand the task to a sub-agent or another behavior file.</summary>
    public const string Delegate = "delegate";

    /// <summary>How many attempts a new Retry node allows.</summary>
    public const int DefaultRetryCount = 3;

    /// <summary>Every kind, in the order the toolbox and the menus list them.</summary>
    public static IReadOnlyList<AbmNodeKind> All { get; } =
    [
        new(Sequence, "Do in order", AbmNodeCategory.Composite, "runs its children one after another, and fails as soon as one fails"),
        new(Fallback, "Try in order", AbmNodeCategory.Composite, "tries its children one after another, and succeeds as soon as one succeeds"),
        new(Parallel, "Do together", AbmNodeCategory.Composite, "runs its children independently, at the same time where you can, and succeeds when all of them succeed"),
        new(Retry, "Retry up to N times", AbmNodeCategory.Decorator, "runs its child again when it fails, at most N times in all"),
        new(Repeat, "Repeat until", AbmNodeCategory.Decorator, "runs its child again and again until its condition holds"),
        new(Guard, "Only while", AbmNodeCategory.Decorator, "runs its child only while its condition holds, and abandons it when the condition stops holding"),
        new(Approval, "Ask approval before", AbmNodeCategory.Decorator, "says what its child is about to do and waits for the user's explicit approval; without it, the node fails"),
        new(Check, "Check", AbmNodeCategory.Leaf, "answers its question from the conversation, your memory or a tool result; it succeeds when the answer is yes, and never acts"),
        new(Action, "Do", AbmNodeCategory.Leaf, "carries out one piece of work: a tool call, an answer, an edit"),
        new(Ask, "Ask the user", AbmNodeCategory.Leaf, "asks its question and waits for the answer"),
        new(Delegate, "Delegate", AbmNodeCategory.Leaf, "hands its task to a sub-agent, or follows the behavior file it links to"),
    ];

    private static readonly Dictionary<string, AbmNodeKind> ById = All.ToDictionary(kind => kind.Id, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="id"/> is one of the eleven.</summary>
    public static bool IsKnown(string id) => ById.ContainsKey(id);

    /// <summary>The kind with <paramref name="id"/>; throws for one that is not known.</summary>
    public static AbmNodeKind Of(string id) => ById[id];

    /// <summary>The kind a keyword names, with a Retry's count; null when the keyword is none of them.</summary>
    /// <remarks>
    /// Read without regard to case or surrounding space, because a person typing the Markdown by
    /// hand should not have to match the capitals. A Retry's count is read from the keyword itself:
    /// "Retry up to 2 times", "Retry up to 1 time".
    /// </remarks>
    public static (AbmNodeKind Kind, int RetryCount)? FromKeyword(string keyword)
    {
        var normalized = string.Join(' ', keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var retry = RetryKeyword().Match(normalized);
        if (retry.Success)
        {
            return (ById[Retry], int.Parse(retry.Groups["count"].Value, CultureInfo.InvariantCulture));
        }

        var kind = All.FirstOrDefault(candidate =>
            candidate.Id != Retry && string.Equals(candidate.Keyword, normalized, StringComparison.OrdinalIgnoreCase));
        return kind is null ? null : (kind, 0);
    }

    /// <summary>The keyword a node of <paramref name="id"/> is written with.</summary>
    public static string KeywordOf(string id, int retryCount) =>
        id == Retry
            ? $"Retry up to {retryCount.ToString(CultureInfo.InvariantCulture)} {(retryCount == 1 ? "time" : "times")}"
            : Of(id).Keyword;

    [GeneratedRegex(@"^retry up to (?<count>\d{1,6}) times?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RetryKeyword();
}
