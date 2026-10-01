using System.Globalization;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// A registration (<c>.adp</c>) in the line form of FBL §8.1: the origin on line 1, headers, the
/// <c>layout:</c> and <c>identities:</c> blocks, and whatever follows them kept as unbound content.
/// Every part keeps its span, so the registration is written by splices like any body (FBL §8.4).
/// </summary>
public sealed class RegistrationDocument
{
    /// <summary>The headers FBL itself defines (FBL §8.1); a binding declares others.</summary>
    public static readonly IReadOnlyList<string> FblHeaders = ["body", "view", "resource"];

    internal RegistrationDocument(BodyText text)
    {
        Text = text;
    }

    internal BodyText Text { get; }

    /// <summary>The origin of the tool type (FBL §8.1, line 1).</summary>
    public string Origin { get; internal set; } = "";

    public IReadOnlyList<RegistrationHeader> Headers { get; internal set; } = [];

    public RegistrationBlock? Layout { get; internal set; }

    public RegistrationBlock? Identities { get; internal set; }

    /// <summary>Where a new block goes: the end of the header region's last line.</summary>
    public int AfterHeaders { get; internal set; }

    /// <summary>Content after the blocks that FBL does not read, kept byte for byte.</summary>
    public Span Unbound { get; internal set; }

    public string? Header(string key) => Headers.FirstOrDefault(h => h.Key == key)?.Value;

    public string? Body => Header("body");

    public string? View => Header("view");

    public string? Resource => Header("resource");

    /// <summary>The positions of the layout block by id; an entry whose value is not two numbers is left out.</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Positions()
    {
        var positions = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        foreach (var entry in Layout?.Entries ?? [])
        {
            if (entry.Position is { } position) positions[entry.Key] = position;
        }
        return positions;
    }

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
            var (line, column) = Text.Position(header.Line.Start);
            findings.Add(new Finding(FindingCodes.UnknownHeader, FindingSeverity.Info,
                $"The header '{header.Key}' is neither FBL's nor the binding's; it is kept as it is.",
                new SourceLocation(fileName, line, column, Text.CodePoints(header.Line.Start, header.Line.End))));
        }
        return findings;
    }

    /// <summary>
    /// Layout entries for ids the model does not have (FBL §8.5): reported as <c>fbl.stale-view-data</c>,
    /// applied to nothing, and removed at the registration's next write.
    /// </summary>
    public IReadOnlyList<Finding> StaleEntries(IReadOnlySet<string> ids, string fileName)
    {
        var findings = new List<Finding>();
        foreach (var entry in Layout?.Entries ?? [])
        {
            if (ids.Contains(entry.Key)) continue;
            var (line, column) = Text.Position(entry.KeySpan.Start);
            findings.Add(new Finding(FindingCodes.StaleViewData, FindingSeverity.Info,
                $"The layout keeps a position for '{entry.Key}', which the file no longer has; it is removed at the next save of the registration.",
                new SourceLocation(fileName, line, column, Text.CodePoints(entry.KeySpan.Start, entry.KeySpan.End))));
        }
        return findings;
    }

    /// <summary>Reads a registration. The line form has no syntax error: anything not understood is unbound content.</summary>
    public static RegistrationDocument Read(byte[] bytes)
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
            if (BlockName(content) is not null) break;
            var separator = content.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0 || char.IsWhiteSpace(content[0])) break;
            headers.Add(new RegistrationHeader(content[..separator], content[(separator + 2)..].Trim(), new Span(line.Start, line.End)));
            document.AfterHeaders = line.End;
        }
        document.Headers = headers;
        while (index < lines.Count)
        {
            var line = lines[index];
            var content = text.Text(line.Start, line.ContentEnd);
            if (content.Trim().Length == 0)
            {
                index++;
                continue;
            }
            var name = BlockName(content);
            if (name is null || (name == "layout" && document.Layout is not null) || (name == "identities" && document.Identities is not null)) break;
            var block = ReadBlock(text, name, ref index);
            if (name == "layout") document.Layout = block;
            else document.Identities = block;
        }
        var unboundStart = index < lines.Count ? lines[index].Start : text.Length;
        document.Unbound = new Span(unboundStart, text.Length);
        return document;
    }

    private static string? BlockName(string content) => content.TrimEnd(' ', '\t') switch
    {
        "layout:" => "layout",
        "identities:" => "identities",
        _ => null,
    };

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

public sealed record RegistrationHeader(string Key, string Value, Span Line);

/// <summary>A block: its name line, its whole span, and its entries in order.</summary>
public sealed record RegistrationBlock(string Name, Span NameLine, Span Span, IReadOnlyList<RegistrationEntry> Entries);

/// <summary>One <c>key: value</c> entry of a block, the key being everything before the line's last <c>": "</c>.</summary>
public sealed record RegistrationEntry(string Key, string Value, Span KeySpan, Span ValueSpan, Span Line, int Indent)
{
    /// <summary>A layout entry's <c>x y</c>, null when the value is not two numbers.</summary>
    public (double X, double Y)? Position
    {
        get
        {
            var parts = Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return null;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return null;
            return (x, y);
        }
    }
}
