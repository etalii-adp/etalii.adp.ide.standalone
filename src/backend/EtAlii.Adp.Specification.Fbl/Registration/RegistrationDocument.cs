using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// A registration (<c>.adp</c>) in the line form of FBL §8.1: the origin on line 1, headers, the
/// <c>layout:</c> and <c>identities:</c> blocks and the blocks the binding declares, and whatever follows them kept as
/// unbound content. A block the binding does not declare is read past and kept as it is (FBL 0.7 §8.1). Every part keeps
/// its span, so the registration is written by splices like any body (FBL §8.4).
/// </summary>
internal sealed partial class RegistrationDocument
{
    /// <summary>The headers FBL itself defines (FBL §8.1); a binding declares others.</summary>
    private static readonly IReadOnlyList<string> FblHeaders = ["body", "view", "resource"];

    private RegistrationDocument(BodyText text)
    {
        Text = text;
    }

    internal BodyText Text { get; }

    /// <summary>The origin of the tool type (FBL §8.1, line 1).</summary>
    public string Origin { get; private set; } = "";

    public IReadOnlyList<RegistrationHeader> Headers { get; private set; } = [];

    public RegistrationBlock? Layout { get; private set; }

    public RegistrationBlock? Identities { get; private set; }

    /// <summary>Every block read, in the order the registration holds them, those the binding does not declare included.</summary>
    private IReadOnlyList<RegistrationBlock> Blocks { get; set; } = [];

    /// <summary>The blocks neither FBL nor the binding declares (FBL 0.7 §8.1): kept as they are written.</summary>
    private IReadOnlyList<RegistrationBlock> Undeclared { get; set; } = [];

    /// <summary>The block <paramref name="name"/>, when the registration has it and it is FBL's or declared.</summary>
    public RegistrationBlock? Block(string name) => Blocks.FirstOrDefault(block => block.Name == name && !Undeclared.Contains(block));

    /// <summary>Where a new block goes: the end of the header region's last line.</summary>
    public int AfterHeaders { get; private set; }

    private string? Header(string key) => Headers.FirstOrDefault(h => h.Key == key)?.Value;

    public string? Body => Header("body");

    public string? View => Header("view");

    public string? Resource => Header("resource");

    /// <summary>The identities block as natural key to id (FBL §8.6).</summary>
    public IReadOnlyDictionary<string, string> IdentityMap() =>
        (Identities?.Entries ?? []).GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    /// <summary>The headers neither FBL nor the binding declares, reported as <c>fbl.unknown-header</c> (FBL §8.1).</summary>
    public IReadOnlyList<Finding> UnknownHeaders(IReadOnlyList<string> declared, string fileName)
    {
        var findings = new List<Finding>();
        foreach (var header in Headers)
        {
            if (FblHeaders.Contains(header.Key) || declared.Contains(header.Key)) continue;
            (int line, int column) = Text.Position(header.Line.Start);
            findings.Add(new Finding(FindingCodes.UnknownHeader, FindingSeverity.Info,
                $"The header '{header.Key}' is neither FBL's nor the binding's; it is kept as it is.",
                new SourceLocation(fileName, line, column, Text.CodePoints(header.Line.Start, header.Line.End))));
        }
        return findings;
    }

    /// <summary>The blocks neither FBL nor the binding declares, reported once each as <c>fbl.unknown-block</c> (FBL 0.7 §8.1).</summary>
    public IReadOnlyList<Finding> UnknownBlocks(string fileName) =>
        Undeclared.Select(block => Located(FindingCodes.UnknownBlock,
            $"The block '{block.Name}' is neither FBL's nor the binding's; it is kept as it is.", block.NameLine with { End = block.NameLine.Start + block.Name.Length + 1 }, fileName)).ToList();

    /// <summary>
    /// Layout entries for ids the model does not have (FBL §8.5), and the entries of the declared blocks keyed by ids
    /// (FBL 0.7 §8.1): reported as <c>fbl.stale-view-data</c>, applied to nothing, and removed at the registration's
    /// next write.
    /// </summary>
    public IReadOnlyList<Finding> StaleEntries(IReadOnlySet<string> ids, string fileName, IReadOnlyList<RegistrationBlockDeclaration>? declarations = null)
    {
        var findings = new List<Finding>();
        foreach (var entry in Layout?.Entries ?? [])
        {
            if (ids.Contains(entry.Key)) continue;
            findings.Add(Located(FindingCodes.StaleViewData,
                $"The layout keeps a position for '{entry.Key}', which the file no longer has; it is removed at the next save of the registration.", entry.KeySpan, fileName));
        }
        foreach (var declaration in declarations ?? [])
        {
            if (!declaration.KeyedByIds || Block(declaration.Name) is not { } block) continue;
            foreach (var entry in block.Entries.Where(entry => !ids.Contains(entry.Key)))
            {
                findings.Add(Located(FindingCodes.StaleViewData,
                    $"The block '{block.Name}' keeps an entry for '{entry.Key}', which the file no longer has; it is removed at the next save of the registration.", entry.KeySpan, fileName));
            }
        }
        return findings;
    }

    private Finding Located(string code, string message, Span span, string fileName)
    {
        (int line, int column) = Text.Position(span.Start);
        return new Finding(code, FindingSeverity.Info, message, new SourceLocation(fileName, line, column, Text.CodePoints(span.Start, span.End)));
    }

    /// <summary>Reads a registration whose binding declares no block. The line form has no syntax error: anything not understood is unbound content.</summary>
    public static RegistrationDocument Read(byte[] bytes) => Read(bytes, []);

    /// <summary>Reads a registration whose binding declares the blocks <paramref name="declared"/> (FBL 0.7 §8.1).</summary>
    public static RegistrationDocument Read(byte[] bytes, IReadOnlyCollection<string> declared)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var text = new BodyText(bytes);
        var document = new RegistrationDocument(text);
        var lines = text.Lines;
        document.Origin = text.Text(text.BomLength, lines[0].ContentEnd).Trim();
        document.AfterHeaders = lines[0].End;
        var index = 1;
        var headers = new List<RegistrationHeader>();
        for (; index < lines.Count; index++)
        {
            var line = lines[index];
            var content = text.Text(line.Start, line.ContentEnd);
            if (content.Trim().Length == 0) continue;
            if (BlockLine().IsMatch(content)) break;
            var separator = content.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0 || char.IsWhiteSpace(content[0])) break;
            headers.Add(new RegistrationHeader(content[..separator], content[(separator + 2)..].Trim(), new Span(line.Start, line.End)));
            document.AfterHeaders = line.End;
        }
        document.Headers = headers;
        var blocks = new List<RegistrationBlock>();
        var undeclared = new List<RegistrationBlock>();
        while (index < lines.Count)
        {
            var line = lines[index];
            var content = text.Text(line.Start, line.ContentEnd);
            if (content.Trim().Length == 0)
            {
                index++;
                continue;
            }
            var match = BlockLine().Match(content);
            if (!match.Success) break;
            var name = match.Groups["name"].Value;
            // A second block of one name ends the blocks: it and what follows are unbound content.
            if (blocks.Any(block => block.Name == name)) break;
            var known = name is "layout" or "identities" || declared.Contains(name);
            var block = ReadBlock(text, name, ref index);
            blocks.Add(block);
            if (!known) undeclared.Add(block);
            else if (name == "layout") document.Layout = block;
            else if (name == "identities") document.Identities = block;
        }
        document.Blocks = blocks;
        document.Undeclared = undeclared;
        return document;
    }

    /// <summary>A line that opens a block (FBL §8.1): a Name and a colon, with nothing but whitespace after it.</summary>
    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_.-]*):[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockLine();

    private static RegistrationBlock ReadBlock(BodyText text, string name, ref int index)
    {
        var header = text.Lines[index];
        var entries = new List<RegistrationEntry>();
        var end = header.End;
        index++;
        while (index < text.Lines.Count)
        {
            var line = text.Lines[index];
            var content = text.Text(line.Start, line.ContentEnd);
            if (content.Length == 0 || !char.IsWhiteSpace(content[0]) || content.Trim().Length == 0) break;
            var separator = content.LastIndexOf(": ", StringComparison.Ordinal);
            if (separator < 0) break;
            var keyStart = content.Length - content.TrimStart().Length;
            var key = content[keyStart..separator];
            var valueText = content[(separator + 2)..];
            var keyOffset = line.Start + System.Text.Encoding.UTF8.GetByteCount(content.AsSpan(0, keyStart));
            var valueOffset = line.Start + System.Text.Encoding.UTF8.GetByteCount(content.AsSpan(0, separator + 2));
            var keySpan = new Span(keyOffset, keyOffset + System.Text.Encoding.UTF8.GetByteCount(key));
            var valueSpan = new Span(valueOffset, valueOffset + System.Text.Encoding.UTF8.GetByteCount(valueText.TrimEnd()));
            entries.Add(new RegistrationEntry(key, valueText.Trim(), keySpan, valueSpan, new Span(line.Start, line.End), keyStart));
            end = line.End;
            index++;
        }
        return new RegistrationBlock(name, new Span(header.Start, header.End), new Span(header.Start, end), entries);
    }
}

internal sealed record RegistrationHeader(string Key, string Value, Span Line);

/// <summary>A block: its name line, its whole span, and its entries in order.</summary>
internal sealed record RegistrationBlock(string Name, Span NameLine, Span Span, IReadOnlyList<RegistrationEntry> Entries);

/// <summary>One <c>key: value</c> entry of a block, the key being everything before the line's last <c>": "</c>.</summary>
internal sealed record RegistrationEntry(string Key, string Value, Span KeySpan, Span ValueSpan, Span Line, int Indent);
