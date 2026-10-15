namespace EtAlii.Adp.Documents;

/// <summary>Where a line comment starts in a line-based language.</summary>
/// <param name="Markers">The texts that start a comment, such as <c>//</c> and <c>#</c>.</param>
/// <param name="OutsideQuotesOnly">
/// Whether a marker inside a double-quoted string is text rather than a comment. A language
/// that does not say so reads every marker as a comment, quoted or not.
/// </param>
/// <param name="NotAfter">
/// A character that, written straight before a marker, makes it text: <c>:</c> keeps the
/// <c>//</c> of a URL out of a comment in a language without quoted strings.
/// </param>
public sealed record CommentPolicy(IReadOnlyList<string> Markers, bool OutsideQuotesOnly, char? NotAfter = null);
