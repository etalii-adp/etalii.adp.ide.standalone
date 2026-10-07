namespace EtAlii.Adp.Diagram.Mindmap.Tests.Parity;

/// <summary>
/// The inline documents of the parity corpus: the validator tests' own maps, and the shapes of a
/// Freeplane file the fixture and the examples do not carry - nodes without ids, formatted text, an
/// empty link, interleaved duplicate ids, a side on a first-level branch, LF line endings.
/// </summary>
internal static class MindmapCorpus
{
    /// <summary>Each document's name in the transcript, and its text.</summary>
    public static IReadOnlyList<(string Name, string Text)> Inline { get; } =
    [
        ("inline/unclosed.mm", "<map version=\"freeplane 1.12.15\"><node TEXT=\"unclosed\"</map>"),
        ("inline/not-a-map.mm", "just some text"),
        ("inline/two-roots.mm", "<map version=\"freeplane 1.12.15\">\n<node TEXT=\"one\" ID=\"ID_1\"/>\n<node TEXT=\"two\" ID=\"ID_2\"/>\n</map>\n"),
        ("inline/unnamed-root.mm", "<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\"><node TEXT=\"a child\" ID=\"ID_2\"/></node></map>"),
        ("inline/blank-root.mm", "<map version=\"freeplane 1.12.15\">\n<node TEXT=\"   \">\n<node TEXT=\"a child\" ID=\"ID_2\"/>\n</node>\n</map>\n"),
        ("inline/empty-ordinary-node.mm", "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\"><node TEXT=\"\" ID=\"ID_2\"/></node></map>"),
        ("inline/duplicate-id.mm", "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\"><node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>"),
        ("inline/several-rules.mm", "<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\"><node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>"),
        (
            "inline/interleaved-duplicates.mm",
            "<map version=\"freeplane 1.12.15\">\n<node TEXT=\"root\" ID=\"ID_1\">\n<node TEXT=\"a\" ID=\"ID_A\"/>\n<node TEXT=\"b\" ID=\"ID_B\">\n<node TEXT=\"b again\" ID=\"ID_B\"/>\n</node>\n" +
            "<node TEXT=\"a again\" ID=\"ID_A\"/>\n<node TEXT=\"a thrice\" ID=\"ID_A\"/>\n<node TEXT=\"root again\" ID=\"ID_1\"/>\n</node>\n</map>\n"
        ),
        (
            "inline/without-ids.mm",
            "<map version=\"freeplane 1.12.15\">\n<node TEXT=\"centre\">\n<node TEXT=\"left\" POSITION=\"left\">\n<node TEXT=\"deep\"/>\n</node>\n<node TEXT=\"right\" POSITION=\"bottom_or_right\" FOLDED=\"true\">\n<node TEXT=\"hidden\"/>\n</node>\n</node>\n</map>\n"
        ),
        (
            "inline/rich-and-links.mm",
            "<map version=\"freeplane 1.12.15\">\r\n<node TEXT=\"root\" ID=\"ID_R\" LINK=\"\">\r\n" +
            "<node ID=\"ID_F\"><richcontent TYPE=\"NODE\">\n<html>\n<head/>\n<body>\n<p>Formatted</p>\n<p>  text   here </p>\n</body>\n</html>\n</richcontent>\r\n" +
            "<richcontent TYPE=\"NOTE\">\n<html>\n<head/>\n<body>\n<p>A note</p>\n</body>\n</html>\n</richcontent>\r\n</node>\r\n" +
            "<node TEXT=\"empty note\" ID=\"ID_E\"><richcontent TYPE=\"NOTE\"><html><head/><body/></html></richcontent></node>\r\n" +
            "<node TEXT=\"web\" ID=\"ID_W\" LINK=\"https://example.org/a?b=1&amp;c=2\" FOLDED=\"false\"/>\r\n" +
            "</node>\r\n</map>\r\n"
        ),
    ];
}
