using System.Text.RegularExpressions;
using System.Xml;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// Whether a text is well-formed XML (xml-editor Requirement 4): the text is read to its end or
/// its first error, and that error becomes one finding where the reader stopped, with the
/// platform's sentence - which already names both tags of a mismatch, the repeated attribute and
/// the undeclared prefix. A reader that stops at the first error reports one; the status says the
/// rest was not checked rather than pretending to more.
/// </summary>
internal static partial class WellFormedness
{
    public const string Source = "XML";
    public const string NotChecked = "Not well-formed: the rest was not checked.";

    /// <summary>What reading the text found: at most one finding, and whether the reader stopped short.</summary>
    public sealed record Outcome(IReadOnlyList<TextFinding> Findings, bool Stopped);

    public static Outcome Check(string content, string fullPath, ProjectXmlResolver resolver, TextPositions positions)
    {
        var refusedBefore = resolver.Refused.Count;
        try
        {
            using var reader = XmlReaders.Document(content, fullPath, resolver);
            while (reader.Read())
            {
            }

            return new Outcome([], Stopped: false);
        }
        catch (XmlException exception)
        {
            (int from, int to) = positions.NameAt(exception.LineNumber, exception.LinePosition);

            // A reference the resolver would not read stops the reader too - an external DTD's
            // entities cannot be known without it. That is not a mistake in this text, so it is
            // said as information where the reader stopped, naming the reference and why.
            if (resolver.Refused.Count > refusedBefore)
            {
                var refused = resolver.Refused[refusedBefore];
                return new Outcome(
                    [new TextFinding(from, to, TextFindingSeverity.Info, $"'{refused.Reference}' {refused.Reason}, so the document could not be read past it.", Source)],
                    Stopped: true);
            }

            return new Outcome([new TextFinding(from, to, TextFindingSeverity.Error, SentenceOf(exception), Source)], Stopped: true);
        }
    }

    /// <summary>
    /// The sentence the declaration's encoding warrants, or null when it agrees with how the file
    /// is stored (xml-editor Requirement 4.5). The family's buffer stores UTF-8, with or without a
    /// byte-order mark; a declaration of another encoding agrees when every character is ASCII,
    /// which reads the same in UTF-8 and in every encoding that extends ASCII.
    /// </summary>
    public static TextFinding? DeclaredEncoding(string content, TextFileBuffer? stored)
    {
        var declaration = Declaration().Match(content);
        if (!declaration.Success || stored is null)
        {
            return null;
        }

        var group = declaration.Groups["encoding"];
        var declared = group.Value.Trim();
        var normalised = declared.ToUpperInvariant().Replace("_", "-", StringComparison.Ordinal);
        if (normalised is "UTF-8" or "UTF8")
        {
            return null;
        }

        var asciiOnly = stored.Content.All(character => character < 0x80);
        var extendsAscii = normalised is "US-ASCII" or "ASCII" || normalised.StartsWith("ISO-8859-", StringComparison.Ordinal) || normalised.StartsWith("WINDOWS-125", StringComparison.Ordinal) || normalised is "LATIN1" or "CP1252";
        if (asciiOnly && extendsAscii && stored.EncodingName == "UTF-8")
        {
            return null;
        }

        return new TextFinding(
            group.Index,
            group.Index + group.Length,
            TextFindingSeverity.Warning,
            $"The declaration says the file is {declared}, but it is stored as {stored.EncodingName}.",
            Source);
    }

    /// <summary>The platform's sentence without its trailing "Line n, position m.", which the underline already shows.</summary>
    private static string SentenceOf(XmlException exception) =>
        TrailingPosition().Replace(exception.Message, "").Trim();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex TrailingPosition();

    [GeneratedRegex("""^﻿?<\?xml\s[^>]*?\bencoding\s*=\s*(["'])(?<encoding>[^"']*)\1""")]
    private static partial Regex Declaration();
}
