using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// One <c>.ghg</c> body, read and written through FBL: the module's binding
/// (<c>gartner-hype-cycle-graph.fbl</c>, embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module parses or splices YAML any more.</b> Reading is the library's reading of
/// the binding's rules; every write is a <see cref="ModelChange"/> the library plans as splices in the
/// body's own conventions (FBL §6), so an unchanged document stays byte-identical and unknown keys,
/// comments and blank lines survive because no splice touches them.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back. The library's own
/// history is per open body, and a command opens a fresh one on the cached text each time.
/// </para>
/// </remarks>
public sealed class GhgBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private readonly OpenBody _body;
    private int[]? _lineStarts;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, FblModel Model)? _model;
    private (byte[] Bytes, IReadOnlyList<Line> Lines)? _lines;
    private (byte[] Bytes, DislModel Model)? _disl;

    private GhgBody(OpenBody body)
    {
        _body = body;
    }

    /// <summary>The binding every <c>.ghg</c> body is read and written with.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's lines as they are now, each with its own ending.</summary>
    public IReadOnlyList<Line> Lines
    {
        get
        {
            if (_lines is not { } lines || lines.Bytes != _body.Bytes)
            {
                _lines = lines = (_body.Bytes, LineDocument.Parse(Text).Lines);
            }

            return lines.Lines;
        }
    }

    /// <summary>What the binding reads from the body now.</summary>
    public FblModel Model
    {
        get
        {
            if (_model is not { } model || model.Bytes != _body.Bytes)
            {
                _model = model = (_body.Bytes, _body.Model);
            }

            return model.Model;
        }
    }

    /// <summary>
    /// The DISL model of what the binding reads now (<see cref="DislModelBuilder"/>, under the bundled
    /// definition): what the context menus and the property rows are derived from.
    /// </summary>
    public DislModel Disl
    {
        get
        {
            if (_disl is not { } model || model.Bytes != _body.Bytes)
            {
                _disl = model = (_body.Bytes, DislModelBuilder.From(Model, GhgDefinition.Specification));
            }

            return model.Model;
        }
    }

    /// <summary>A body for <paramref name="text"/>. Never throws on content: an unreadable body reads as such.</summary>
    public static GhgBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new GhgBody(OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding, new FblOptions { FileName = "body.ghg" }));
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    internal LineRange LinesOf(Span span)
    {
        var starts = _lineStarts ??= LineStarts(_body.Bytes);
        var first = LineIndex(starts, span.Start);
        var last = LineIndex(starts, Math.Max(span.Start, span.End - 1));
        return new LineRange(first, last);
    }

    /// <summary>
    /// The 0-based line of <paramref name="key"/> in the entry on <paramref name="range"/>: the line
    /// that starts with it at the entry's own key indentation, else the entry's first line.
    /// </summary>
    internal int KeyLine(LineRange range, string key)
    {
        var lines = Lines;
        if (range.Start >= lines.Count)
        {
            return range.Start;
        }

        var first = lines[range.Start].Text;
        var indent = first.Length - first.TrimStart(' ', '-').Length;
        for (var index = range.Start; index <= Math.Min(range.End, lines.Count - 1); index++)
        {
            var text = lines[index].Text;
            if (text.Length > indent && text.AsSpan(0, indent).Trim(" -").IsEmpty && text.AsSpan(indent).StartsWith(key + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return range.Start;
    }

    /// <summary>
    /// The element of <paramref name="type"/> the module knows by <paramref name="id"/> and
    /// <paramref name="range"/>: by its stored id when only one entry has it, else by the line it starts on.
    /// </summary>
    internal FblElement? Find(string type, string id, LineRange range)
    {
        var typed = Model.Elements.Where(element => element.Type == type).ToList();
        var byId = typed.Where(element => GhgParser.Text(element.Attributes.GetValueOrDefault("storedId")) == id).ToList();
        return byId.Count == 1 ? byId[0] : typed.FirstOrDefault(element => element.Line - 1 == range.Start);
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal GhgEdit Change(ModelChange change)
    {
        var result = _body.Change(change);
        _lineStarts = null;
        return result is PlanResult.Refused refused ? GhgEdit.Refused(refused.Reason) : GhgEdit.Applied;
    }

    /// <summary>Sets attributes of the element the module knows by <paramref name="id"/> and <paramref name="range"/>.</summary>
    internal GhgEdit Set(string type, string id, LineRange range, IReadOnlyDictionary<string, object?> attributes)
    {
        if (Find(type, id, range) is not { } element)
        {
            return GhgEdits.Gone();
        }

        return attributes.Count == 0 ? GhgEdit.Applied : Change(new ModelChange.Set(element.Id, attributes));
    }

    /// <summary>Removes the element the module knows by <paramref name="id"/> and <paramref name="range"/>, with what cascades from it.</summary>
    internal GhgEdit Remove(string type, string id, LineRange range) =>
        Find(type, id, range) is { } element ? Change(new ModelChange.Remove(element.Id)) : GhgEdits.Gone();

    private static int[] LineStarts(byte[] bytes)
    {
        List<int> starts = [0];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n')
            {
                starts.Add(index + 1);
            }
        }

        return [.. starts];
    }

    private static int LineIndex(int[] starts, int offset)
    {
        var index = Array.BinarySearch(starts, offset);
        return index >= 0 ? index : ~index - 1;
    }

    private static FblBinding LoadBinding()
    {
        var assembly = typeof(GhgBody).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("gartner-hype-cycle-graph.fbl", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), name, out var document);
        if (document is null)
        {
            throw new InvalidOperationException($"The hype cycle graph's FBL binding does not load: {string.Join("; ", problems)}");
        }

        return document.Bindings["ghg"];
    }
}
