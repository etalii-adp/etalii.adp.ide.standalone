using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// One <c>.tml</c> body, read through FBL: the module's binding (<c>timeline.fbl</c>, embedded)
/// applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model built from that reading under
/// the bundled definition (timeline-disl-fbl Requirement 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which entries are periods, moments and relations, and what each value reads as, is the
/// binding's.</b> Every value is the text it was written with (FBL 0.5 section 5.2): a label is a
/// string and reads its written text unasked, and the binding asks for the text of a begin, an end
/// and a row, so <c>yes</c>, <c>1.0</c> and <c>null</c> stay what the author wrote. The slots are
/// read with the attribute types the definition gives them (<see cref="DislAttributeTypes"/>).
/// </para>
/// <para>
/// <b>Every write is a <see cref="ModelChange"/></b> the library plans as splices in the body's own
/// conventions (FBL section 6, timeline-disl-fbl Requirement 3.1): a value's own span in its own style, a
/// new key where the binding's <c>keys</c> put it, a new entry indented as the entries already there. No
/// code here builds a line of YAML, so an unchanged document stays byte-identical and a comment, an
/// unknown key or a blank line survives because no splice touches it.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back
/// (<see cref="TimelineEdits"/>). The library's own history is per open body, and a command opens a
/// fresh one on the store's text each time.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read lately rather than reading it once more:
/// the store, the validator and a command each ask for the same text in turn. Each fork is its own
/// body, so a reading of one is never changed by another.
/// </para>
/// </remarks>
public sealed class TimelineBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.tml",
        AttributeType = DislAttributeTypes.Of(TimelineDefinition.Specification),
        TimeAttributes = DislAttributeTypes.TimeAttributes(TimelineDefinition.Specification, LoadedBinding.Value),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private int[]? _lineStarts;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, FblModel Model)? _model;
    private (byte[] Bytes, DislModel Model)? _disl;

    // While a batch is open, the changes asked for and their edits, planned against the body as it was.
    private List<(ModelChange Change, Edit Edit)>? _batch;

    private TimelineBody(OpenBody body)
    {
        _body = body;
    }

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>What the binding reads from the body.</summary>
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
    /// The DISL model of what the binding reads (<see cref="DislModelBuilder"/>, under the bundled
    /// definition): what the menus, the rows and the findings are derived from. Read it, never change it.
    /// </summary>
    public DislModel Disl
    {
        get
        {
            if (_disl is not { } model || model.Bytes != _body.Bytes)
            {
                _disl = model = (_body.Bytes, DislModelBuilder.From(Model, TimelineDefinition.Specification));
            }

            return model.Model;
        }
    }

    /// <summary>A DISL diagram of what the binding reads that is the caller's own: the working diagram an operation changes as it runs.</summary>
    public DislDiagram Working() => DislModelBuilder.From(Model, TimelineDefinition.Specification).Diagram;

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a body the library cannot read reads as unreadable.</summary>
    public static TimelineBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new TimelineBody(known.Fork());

        var body = OpenBody.Open(bytes, LoadedBinding.Value, LoadedOptions.Value);
        Recent.Remember(body.Fork());
        return new TimelineBody(body);
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    public LineRange LinesOf(Span span)
    {
        var starts = _lineStarts ??= LineStarts(_body.Bytes);
        var first = LineIndex(starts, span.Start);
        var last = LineIndex(starts, Math.Max(span.Start, span.End - 1));
        return new LineRange(first, last);
    }

    /// <summary>
    /// The library's id of the entry whose lines start at <paramref name="range"/>: the element (or, with
    /// <paramref name="relation"/>, the relation) the module's model read there. Found by its place rather
    /// than by its written id, because an id written twice is the library's for its first holder only.
    /// </summary>
    internal string? IdAt(LineRange range, bool relation)
    {
        foreach (var element in Model.Elements)
        {
            if ((element.Rule == "connection") != relation) continue;
            if (LinesOf(element.OwnSpan).Start == range.Start) return element.Id;
        }

        return null;
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change)
    {
        if (_batch is not null)
        {
            var planned = _body.Plan(change);
            if (planned is PlanResult.Planned { Edit: var edit }) _batch.Add((change, edit));
            return planned is PlanResult.Refused declined ? EditOutcome.Refused(declined.Reason) : EditOutcome.Applied;
        }

        var result = _body.Change(change);
        _lineStarts = null;
        if (result is not PlanResult.Refused) Recent.Remember(_body.Fork());
        return result is PlanResult.Refused refused ? EditOutcome.Refused(refused.Reason) : EditOutcome.Applied;
    }

    /// <summary>
    /// Runs <paramref name="writes"/>, each a change to a different entry that reads only that entry,
    /// and applies them together: as one edit, read once, when their splices keep apart; else one by
    /// one, in the order they were asked for, as <see cref="Change"/> would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every change is planned against the body as it was before any of them</b>, so an element is
    /// found where it was read. That is what the writes would meet one by one only when each touches its
    /// own entry alone, which is the caller's to promise: Arrange diagram, which sets one row per entry.
    /// </para>
    /// <para>
    /// <b>Why</b>: applying a change reads the whole body again, so N changes one by one cost N readings
    /// (timeline-disl-fbl, Performance: an arrangement that writes many rows is planned as one edit).
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

        foreach ((ModelChange change, _) in batch)
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
        var assembly = typeof(TimelineBody).Assembly;
        using var stream = assembly.GetManifestResourceStream("timeline.fbl")
            ?? throw new InvalidOperationException("The timeline's FBL binding is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document is null || problems.Any(problem => problem.Severity == ProblemSeverity.Error)
            ? throw new InvalidOperationException($"The timeline's FBL binding does not load: {string.Join("; ", problems)}")
            : document.Bindings["timeline"];
    }
}
