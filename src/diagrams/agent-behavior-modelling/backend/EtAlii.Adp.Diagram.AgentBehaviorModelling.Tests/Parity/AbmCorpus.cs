namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The behavior model corpus every parity check runs over: the four examples, and the documents the
/// parser and writer tests state inline, each under a name that says where it came from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The inline documents are the tests' own, byte for byte</b>: a parser test's lines joined with
/// <c>\n</c> and ended with one, as its <c>Parse</c> helper does, and the writer test's documents as
/// written. <see cref="WriterTree"/> is the writer test's own constant, so that one cannot drift; the
/// others are copies, named for the test they were taken from.
/// </para>
/// <para>
/// <b>The examples are read from disk</b> (<see cref="AbmExamples"/>), so they are the shipped files.
/// </para>
/// </remarks>
internal static class AbmCorpus
{
    /// <summary>The writer tests' tree: CRLF, notes under a leaf, prose on both sides and no final newline.</summary>
    public const string WriterTree =
        "# Agent\r\n" +
        "\r\n" +
        "Prose the module never touches.\r\n" +
        "\r\n" +
        "## Behavior\r\n" +
        "\r\n" +
        "- **Do in order:** Work\r\n" +
        "  - **Check:** Ready\r\n" +
        "  - **Try in order:** Options\r\n" +
        "    - **Do:** First\r\n" +
        "      Notes for first.\r\n" +
        "    - **Do:** Second\r\n" +
        "  - **Do:** Finish\r\n" +
        "\r\n" +
        "## After\r\n" +
        "\r\n" +
        "More prose.";

    /// <summary>The inline documents, by name, in the order the transcript records them.</summary>
    public static IReadOnlyList<(string Name, string Text)> Inline { get; } =
    [
        ("inline/parser/reads-the-tree", Lines(
            "# Agent",
            "",
            "## Behavior",
            "",
            "- **Try in order:** Help",
            "  - **Check:** The user asked",
            "  - **Do in order:** Work",
            "    - **Do:** Act",
            "    - **Retry up to 2 times:** Test",
            "      - **Ask the user:** Which test?")),
        ("inline/parser/reads-notes", Lines(
            "## Behavior",
            "- **Do in order:** Work",
            "  First line of the notes.",
            "",
            "    Indented further.",
            "  - **Do:** Act")),
        ("inline/parser/only-the-behavior-section", Lines(
            "# Agent",
            "- not a node: the list before the section",
            "```",
            "## Behavior",
            "- **Do:** inside a code block",
            "```",
            "## Behaviour",
            "- **Do:** The real one",
            "## Other",
            "- **Do:** After the section")),
        ("inline/parser/keyword-colon-outside", Lines("## Behavior", "- **Do**: Act")),
        ("inline/parser/keyword-lowercase-star", Lines("## Behavior", "* **do:** Act")),
        ("inline/parser/keyword-plus-no-space", Lines("## Behavior", "+ **Do:**Act")),
        ("inline/parser/no-keyword", Lines("## Behavior", "- Just do the thing")),
        ("inline/parser/tab-indented-child", Lines("## Behavior", "- **Do in order:** Work", "\t- **Do:** Act")),
        ("inline/parser/thematic-break-dashes", Lines("## Behavior", "---")),
        ("inline/parser/thematic-break-spaced", Lines("## Behavior", "- - -")),
        ("inline/parser/thematic-break-stars-spaced", Lines("## Behavior", "* * *")),
        ("inline/parser/thematic-break-stars", Lines("## Behavior", "***")),
        ("inline/parser/thematic-break-wide", Lines("## Behavior", "-  -  -")),
        ("inline/parser/thematic-break-four", Lines("## Behavior", "- - - -")),
        ("inline/parser/thematic-break-tabs", Lines("## Behavior", "-\t-\t-")),
        ("inline/parser/thematic-break-indented", Lines("## Behavior", "   * * *")),
        ("inline/parser/no-section", Lines("# Notes", "- **Do:** Something")),
        ("inline/writer/tree", WriterTree),
        ("inline/writer/lf-tree", "## Behavior\n- **Do in order:** Work\n  - **Do:** Act\n"),
        ("inline/writer/blank-lines-no-final-newline", "## Behavior\n\n- **Do in order:** Work\n\n  - **Do:** Act"),
        ("inline/writer/no-tree-yet", "# Agent\r\n\r\nNo tree yet.\r\n"),
        ("inline/writer/empty-section", "## Behavior\n\nNothing yet.\n"),
    ];

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";
}
