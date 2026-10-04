using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// What to call a newly added element, read off the elements already sitting beside it.
/// </summary>
/// <remarks>
/// <para>
/// A new node used to be named by asking the user in a dialog before it existed. It is named
/// from its siblings instead, so the node can be created at once and the user can edit its
/// label in place - which is the same gesture every other rename uses, on a node they can
/// already see.
/// </para>
/// <para>
/// <b>The rule follows the siblings rather than imposing a default</b>, which is the user's
/// ruling and the reason this is worth a shared class rather than a constant. Three readings,
/// in order, and the first that applies wins:
/// </para>
/// <list type="number">
/// <item>
/// <b>The siblings are numbered.</b> <c>Phase 1</c>, <c>Phase 2</c> give <c>Phase 3</c> - the
/// shared stem plus one past the highest number, not the count, so deleting <c>Phase 2</c> and
/// adding again gives <c>Phase 4</c> rather than a second <c>Phase 3</c>.
/// </item>
/// <item>
/// <b>The siblings share a last word.</b> <c>Measure pass</c>, <c>Arrange pass</c> give
/// <c>New pass</c>. The shared word has to be the LAST one and there have to be at least two
/// siblings agreeing on it; one sibling is not a pattern, it is a coincidence.
/// </item>
/// <item>
/// <b>Neither.</b> The fallback name, made unique the same way a file manager does it.
/// </item>
/// </list>
/// <para>
/// <b>Every branch ends in the same uniqueness pass</b>, because a derived name that collides
/// is worse than a dull one: two siblings called <c>New pass</c> are indistinguishable in the
/// tree and in every test that looks one up by text.
/// </para>
/// <para>
/// Case is preserved from the siblings and compared ordinally-ignoring-case: <c>Phase</c> and
/// <c>phase</c> are the same stem, and the new name copies whichever spelling the highest
/// numbered sibling used, because that is the one the author most recently chose.
/// </para>
/// </remarks>
public static partial class SiblingNaming
{
    /// <summary>The name for a new element among <paramref name="siblings"/>.</summary>
    /// <param name="siblings">The names already in use beside the new element. Order is irrelevant.</param>
    /// <param name="fallback">What to call it when the siblings show no pattern at all.</param>
    public static string NextName(IEnumerable<string> siblings, string fallback = "Node")
    {
        var taken = siblings.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToArray();

        return MakeUnique(NumberedContinuation(taken) ?? SharedLastWord(taken) ?? fallback, taken);
    }

    /// <summary>
    /// One past the highest number among siblings sharing a stem, or null when they are not
    /// numbered.
    /// </summary>
    private static string? NumberedContinuation(IReadOnlyList<string> taken)
    {
        var numbered = taken
            .Select(name => NumberedExpression().Match(name))
            .Where(match => match.Success)
            .Select(match => (Stem: match.Groups["stem"].Value, Number: long.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        if (numbered.Length == 0)
        {
            return null;
        }

        // The stem the most siblings agree on, so one stray "Draft 2" among five "Phase n"
        // does not decide the answer. Ties break on the highest number, which is the most
        // recently added of the tied stems.
        var winner = numbered
            .GroupBy(entry => entry.Stem, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(entry => entry.Number))
            .First();

        (string stem, long number) = winner.OrderByDescending(entry => entry.Number).First();
        return $"{stem} {number + 1}";
    }

    /// <summary>
    /// <c>New &lt;word&gt;</c> when at least two siblings end in the same word, or null.
    /// </summary>
    private static string? SharedLastWord(IReadOnlyList<string> taken)
    {
        var lastWords = taken
            .Select(name => name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length > 1)
            .Select(words => words[^1])
            .ToArray();

        var shared = lastWords
            .GroupBy(word => word, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        return shared is null ? null : $"New {shared.First()}";
    }

    /// <summary>The name, or the first of "name 2", "name 3"… that no sibling already holds.</summary>
    private static string MakeUnique(string name, IReadOnlyList<string> taken)
    {
        if (!taken.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return name;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name} {suffix}";
            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
    }

    /// <summary>A name ending in a number, split into the stem before it and the number itself.</summary>
    [GeneratedRegex(@"^(?<stem>.*\S)\s+(?<number>\d{1,15})$")]
    private static partial Regex NumberedExpression();
}
