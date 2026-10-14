using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;

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
/// <b>Writing is not here yet</b>: the commands still splice the file's lines through
/// <see cref="TimelineWriter"/>, on the line ranges this body's reading gives (<see cref="LinesOf"/>).
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

    private TimelineBody(OpenBody body)
    {
        _body = body;
    }

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

    /// <summary>The open bodies of the last few texts read, by their bytes: forks no one edits, only forked again.</summary>
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
