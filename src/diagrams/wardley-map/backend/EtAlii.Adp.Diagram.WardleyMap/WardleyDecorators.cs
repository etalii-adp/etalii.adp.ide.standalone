namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The five decorators, and how they are written and read back (Requirement 6.3).
/// </summary>
/// <remarks>
/// One list, because three places need the same one in the same order - the menu's toggles, the
/// property grid's single "what is set" row, and the command that sets the whole set - and three
/// copies of five words is three chances for them to disagree about whether `ecosystem` comes
/// before `build`.
/// </remarks>
public static class WardleyDecorators
{
    /// <summary>The five, in the order the DSL documents them.</summary>
    public static IReadOnlyList<WardleyDecorator> All { get; } =
    [
        WardleyDecorator.Market,
        WardleyDecorator.Ecosystem,
        WardleyDecorator.Build,
        WardleyDecorator.Buy,
        WardleyDecorator.Outsource,
    ];

    /// <summary>The word the document writes for one decorator.</summary>
    public static string Spell(WardleyDecorator decorator) => decorator.ToString().ToLowerInvariant();

    /// <summary>The decorators a component carries, as one comma-separated line for a reader.</summary>
    public static string Join(IEnumerable<WardleyDecorator> decorators) =>
        string.Join(", ", decorators.Select(Spell));

    /// <summary>
    /// Reads a comma-separated line back, or null when a word in it is not one of the five.
    /// </summary>
    /// <remarks>
    /// Null rather than "the ones it recognised": silently dropping a misspelled `outsorce`
    /// would leave the user looking at a row that did not do what they typed and did not say so.
    /// </remarks>
    public static IReadOnlyList<WardleyDecorator>? Parse(string value, out string unknown)
    {
        unknown = "";
        var words = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<WardleyDecorator>();

        foreach (var word in words)
        {
            var match = All
                .Cast<WardleyDecorator?>()
                .FirstOrDefault(candidate => string.Equals(Spell(candidate!.Value), word, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown = word;
                return null;
            }

            if (!result.Contains(match.Value))
            {
                result.Add(match.Value);
            }
        }

        return result;
    }

    /// <summary>The five, listed for an error message.</summary>
    public static string Vocabulary => string.Join(", ", All.Select(Spell));
}
