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
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately (<see cref="OpenBody.Fork"/>)
/// rather than reading it once more: a command's copy of the cached document, the validator's
/// reading of the text the store holds and an undo's restored text all cost no reading. Each fork
/// is its own body, so an edit to one is never seen by another.
/// </para>
/// </remarks>
public sealed class GhgBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private int[]? _lineStarts;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, FblModel Model)? _model;
    private (byte[] Bytes, IReadOnlyList<Line> Lines)? _lines;
    private (byte[] Bytes, DislModel Model)? _disl;

    // While a batch is open, the changes asked for and their edits, planned against the body as it was.
    private List<(ModelChange Change, Edit Edit)>? _batch;

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
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new GhgBody(known.Fork());

        var body = OpenBody.Open(bytes, Binding, new FblOptions { FileName = "body.ghg" });
        Recent.Remember(body.Fork());
        return new GhgBody(body);
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
        if (_batch is not null)
        {
            var planned = _body.Plan(change);
            if (planned is PlanResult.Planned { Edit: var edit }) _batch.Add((change, edit));
            return planned is PlanResult.Refused declined ? GhgEdit.Refused(declined.Reason) : GhgEdit.Applied;
        }

        var result = _body.Change(change);
        _lineStarts = null;
        if (result is not PlanResult.Refused) Recent.Remember(_body.Fork());
        return result is PlanResult.Refused refused ? GhgEdit.Refused(refused.Reason) : GhgEdit.Applied;
    }

    /// <summary>
    /// Runs <paramref name="writes"/>, each a change to a different entry that reads only that entry,
    /// and applies them together: as one edit, read once, when their splices keep apart; else one by
    /// one, in the order they were asked for, as <see cref="Change"/> would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every change is planned against the body as it was before any of them</b>, so an element is
    /// found where it was read and a refusal is the one that body gives. That is what the writes would
    /// meet one by one only when each touches its own entry alone, which is the caller's to promise:
    /// Arrange, which rewrites rows bottom-up for exactly that reason.
    /// </para>
    /// <para>
    /// <b>Why</b>: applying a change reads the whole body again, so N changes one by one cost N
    /// readings, which made arranging a large graph take seconds.
    /// </para>
    /// </remarks>
    internal void Batch(Action writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (_batch is not null) throw new InvalidOperationException("A batch is already open on this body.");

        _batch = [];
        List<(ModelChange Change, Edit Edit)> batch;
        try
        {
            writes();
        }
        finally
        {
            batch = _batch;
            _batch = null;
        }

        if (Combined(batch) is { } combined)
        {
            _body.Apply(combined);
            _lineStarts = null;
            Recent.Remember(_body.Fork());
            return;
        }

        foreach (var (change, _) in batch)
        {
            Change(change);
        }
    }

    /// <summary>The edits of a batch as one, when no two of them touch and none needs a snapshot to undo; else null.</summary>
    private static Edit? Combined(List<(ModelChange Change, Edit Edit)> batch)
    {
        var edits = batch.Select(planned => planned.Edit).Where(edit => edit.Splices.Count > 0).ToList();
        if (edits.Count == 0 || edits.Any(edit => edit.Snapshot)) return null;

        edits.Sort((left, right) => left.Splices.Min(splice => splice.Start).CompareTo(right.Splices.Min(splice => splice.Start)));
        for (var index = 1; index < edits.Count; index++)
        {
            if (edits[index - 1].Splices.Max(splice => splice.End) >= edits[index].Splices.Min(splice => splice.Start)) return null;
        }

        return new Edit([.. edits.SelectMany(edit => edit.Splices)]);
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

    /// <summary>The open bodies of the last few texts read or written, by their bytes: forks no one edits, only forked again.</summary>
    private sealed class RecentBodies
    {
        private const int Capacity = 8;

        private readonly LinkedList<(int Hash, OpenBody Body)> _bodies = [];
        private readonly Lock _lock = new();

        public OpenBody? Find(byte[] bytes)
        {
            var hash = Hash(bytes);
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (node.Value.Hash != hash || !node.Value.Body.Bytes.AsSpan().SequenceEqual(bytes)) continue;
                    _bodies.Remove(node);
                    _bodies.AddFirst(node);
                    return node.Value.Body;
                }
            }

            return null;
        }

        public void Remember(OpenBody body)
        {
            var hash = Hash(body.Bytes);
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (node.Value.Hash == hash && node.Value.Body.Bytes.AsSpan().SequenceEqual(body.Bytes))
                    {
                        _bodies.Remove(node);
                        break;
                    }
                }

                _bodies.AddFirst((hash, body));
                while (_bodies.Count > Capacity)
                {
                    _bodies.RemoveLast();
                }
            }
        }

        private static int Hash(byte[] bytes)
        {
            var hash = new HashCode();
            hash.AddBytes(bytes);
            return hash.ToHashCode();
        }
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
