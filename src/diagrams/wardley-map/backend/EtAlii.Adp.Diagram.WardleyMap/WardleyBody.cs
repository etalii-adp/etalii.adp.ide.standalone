using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One <c>.owm</c> body, read through FBL: the module's binding (<c>wardley-map.fbl</c> with its grammar
/// <c>wardley-map.lark</c>, embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model of that reading
/// built by <see cref="DislModelBuilder"/> through the definition's <c>typeMap</c> (wardley-map-disl-fbl task 11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module parses a map any more.</b> The reading is the library's reading of the binding's rules. What
/// the module still needs in its own vocabulary, the <see cref="WardleyMap"/> the writer, the commands, the mapper and the
/// identities work on until task 12 writes through the binding, is projected from that reading, statement by statement.
/// </para>
/// <para>
/// <b>The model is built from the reading</b>, each element under the id the store keeps for its kind and key
/// (<see cref="WardleyIdentities"/>), which are the binding's <c>sidecar</c> kind and key; an element the store keeps
/// none for has one no identity can equal. A statement that names an element by its name, a link's end, an
/// evolution's or a pipeline's <c>component</c>, names the first holder of that name in document order, as the module
/// has since task 2 (ruling 3); one that names no element has no end, and the name as written is kept for
/// <c>writtenEnd</c> (<see cref="WardleyFunctions"/>). The elements come in the order the identities are reconciled in:
/// the components, the links, each pipeline followed by its components, the notes, the annotations, the accelerators
/// and the attitudes; then the evolutions and the <c>url</c> definitions.
/// </para>
/// <para>
/// <b>The grammar family reads every slot as its text</b> (FBL §5.2), and a value the definition types with one of its
/// own data types (<c>Fraction</c>, <c>MapPosition</c>, <c>LabelOffset</c>) is not converted by DISL's reading, so
/// <see cref="Typed"/> gives each its value: a number, a whole number, a flag, a pair.
/// </para>
/// <para>
/// <b>Five things are read as the module read them before, until task 12</b>, each where the reading differs from what
/// the transcript records: a line the binding does not read is reported in the module's own sentence
/// (<see cref="Sentence"/>); a statement followed on its line by text the grammar does not read, as an <c>evolve</c>
/// with a <c>label</c> is, is passed over whole, as the parser could not read it; what follows a pipeline whose block never closes is inside it, as the module has always
/// read it, so that only its components are read there; a link whose source holds <c>-</c> or <c>+</c> is passed
/// over, the defect of finding B13 that task 12 removes by construction with the module's own link grammar; and the
/// first line of a text that begins with a byte order mark is passed over, as the parser, whose keyword then began with
/// the mark, passed it over.
/// </para>
/// </remarks>
internal sealed class WardleyBody
{
    /// <summary>The finding of a text the grammar reads as no unit of it (FBL §4.9).</summary>
    private const string UnparsedText = "fbl.unparsed-text";

    /// <summary>The finding of a <c>}</c> that closes no block (FBL §4.9).</summary>
    private const string UnmatchedClose = "fbl.unmatched-close";

    /// <summary>The finding of a block whose <c>}</c> never comes (FBL §4.9).</summary>
    private const string UnclosedBlock = "fbl.unclosed-block";

    /// <summary>
    /// The host attribute holding a link as the menus choose it: its source, target and kind as written, joined by
    /// <see cref="WardleyIdentityKeys.Separator"/>, which every repeat of one link shares.
    /// </summary>
    private const string ChoiceKey = "choiceKey";

    private const string InsidePipeline = "This line inside a pipeline is not a component of it, so it was passed over.";

    private const string NoStatement = "This line is not a statement of a Wardley map, so it was passed over.";

    /// <summary>The keywords of the statements the module reads, whose line the binding does not read is "this … statement".</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "style", "size", "annotations", "annotation", "evolve", "note", "url", "pioneers", "settlers", "townplanners",
        "accelerator", "deaccelerator", "pipeline", "component", "anchor", "submap",
    };

    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(WardleyBody).Assembly, "wardley-map.fbl", "map"));

    private static readonly FblOptions Options = new() { FileName = "body.owm", Many = ManyOf };

    /// <summary>The bodies read lately, by their text: a menu, a row and a finding of one text read it once.</summary>
    private static readonly Recent Bodies = new();

    private readonly ConditionalWeakTable<IReadOnlyList<WardleyIdentityEntry>, DislDiagram> _models = new();
    private readonly List<FblElement> _read;

    private WardleyBody(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reading = OpenBody.Open(bytes, LoadedBinding.Value, Options).Model;
        var lines = new LineStarts(bytes);
        var problems = new SortedDictionary<int, string>();
        var unclosed = reading.Findings.Where(finding => finding is { Code: UnclosedBlock, Location: not null }).Select(finding => finding.Location!.Line).ToHashSet();
        var blocks = reading.Elements.Where(element => element.Type == "Pipeline")
            .Select(pipeline => (Pipeline: pipeline, Last: unclosed.Contains(pipeline.Line) ? int.MaxValue : lines.LineOf(pipeline.OwnSpan.End - 1)))
            .ToList();

        _read = [];
        var unparsed = reading.Findings.Where(finding => finding is { Code: UnparsedText, Location: not null }).Select(finding => finding.Location!.Line).ToHashSet();
        var marked = text.StartsWith('\uFEFF') && text[1..].Split('\n')[0].Trim() is { Length: > 0 } first && !first.StartsWith("//", StringComparison.Ordinal);
        if (marked) problems.TryAdd(0, NoStatement);
        foreach (var element in reading.Elements)
        {
            if (marked && element.Line == 1) continue;
            if (unparsed.Contains(element.Line)) continue;

            (FblElement? pipeline, int last) = blocks.FirstOrDefault(block => element.Line > block.Pipeline.Line && element.Line <= block.Last);
            if (pipeline is not null && last == int.MaxValue && element.ParentId != pipeline.Id)
            {
                problems.TryAdd(element.Line - 1, InsidePipeline);
                continue;
            }

            if (element.Type is "Dependency" or "Flow" && Text(element, "from").AsSpan().IndexOfAny('-', '+') >= 0)
            {
                problems.TryAdd(element.Line - 1, NoStatement);
                continue;
            }

            _read.Add(element);
        }

        var document = LineDocument.Parse(text);
        foreach (var finding in reading.Findings.Where(finding => finding.Code is UnparsedText or UnmatchedClose && finding.Location is not null))
        {
            var line = finding.Location!.Line;
            var within = blocks.Any(block => line > block.Pipeline.Line && line <= block.Last);
            problems.TryAdd(line - 1, Sentence(document, line, within));
        }

        Outcome = new ParseOutcome<WardleyMap>(Project(_read), [.. problems.Select(problem => new ParseProblem(problem.Key, problem.Value))]);
    }

    /// <summary>The map the body states, with a problem for every line it passed over, in line order.</summary>
    private ParseOutcome<WardleyMap> Outcome { get; }

    /// <summary>The body of <paramref name="document"/>'s text, read once for that text.</summary>
    private static WardleyBody Of(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Bodies.Of(document.Text);
    }

    /// <summary>What <paramref name="document"/> states, with a problem for every line it passed over.</summary>
    public static ParseOutcome<WardleyMap> Read(LineDocument document) => Of(document).Outcome;

    /// <summary>
    /// The model of <paramref name="document"/>, with <paramref name="identities"/>, built once for them; read it, never
    /// change it.
    /// </summary>
    public static DislDiagram Model(LineDocument document, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var body = Of(document);
        return body._models.GetValue(identities, body.Build);
    }

    /// <summary>A model of <paramref name="document"/>, with <paramref name="identities"/>, of its own, for an operation to change as it runs.</summary>
    public static DislDiagram Working(LineDocument document, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        return Of(document).Build(identities);
    }

    /// <summary>The id a menu's choice gives <paramref name="option"/>: a link's <see cref="ChoiceKey"/>, else its model id; its text for anything but an element.</summary>
    public static string ChoiceIdOf(object? option) => option switch
    {
        DislElement element => element.HostAttributes.TryGetValue(ChoiceKey, out var key) && key is string text ? text : element.Id,
        _ => Convert.ToString(option, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>The element written with the wire id <paramref name="id"/>: the identity the store gives it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is { Length: > 0 } ? diagram.ElementById(id) : null;
    }

    /// <summary>
    /// The sentence of the 1-based <paramref name="line"/> the binding does not read: one inside a pipeline's block is not a
    /// component of it; one that starts with a statement's keyword is that statement, which could not be read; any
    /// other is no statement.
    /// </summary>
    private static string Sentence(LineDocument document, int line, bool insidePipeline)
    {
        if (insidePipeline) return InsidePipeline;
        var text = line - 1 < document.Lines.Count ? document.Lines[line - 1].Text.Trim() : "";
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var keyword = text[..end];
        return Keywords.Contains(keyword)
            ? $"This {keyword.ToLowerInvariant()} statement could not be read, so it was passed over."
            : NoStatement;
    }

    /// <summary>The model of this body with <paramref name="identities"/>.</summary>
    private DislDiagram Build(IReadOnlyList<WardleyIdentityEntry> identities)
    {
        var ids = new Ids(identities);
        var holders = new Dictionary<string, string>(StringComparer.Ordinal);
        var given = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in _read.Where(IsPositioned))
        {
            var id = ids.Of(element);
            given[element.Id] = id;
            holders.TryAdd(Text(element, "name"), id);
        }

        foreach (var element in _read.Where(element => !IsPositioned(element)))
        {
            given[element.Id] = element.IdentityKind is null ? element.Id : ids.Of(element);
        }

        var evolution = 0;
        var url = 0;
        var model = new List<FblElement>();
        model.AddRange(_read.Where(element => element.Type is "Title" or "Style" or "Size" or "AnnotationsPosition").Select(Typed));
        model.AddRange(_read.Where(IsPositioned).Select(element => Typed(element) with { Id = given[element.Id], IdIsStored = true, Attributes = Positioned(element) }));
        model.AddRange(_read.Where(element => element.Type is "Dependency" or "Flow").Select(element => Typed(element) with { Id = given[element.Id], IdIsStored = true, Attributes = Link(element, holders) }));
        foreach (var pipeline in _read.Where(element => element.Type is "Pipeline" or "LegacyPipeline"))
        {
            model.Add(Typed(pipeline) with { Id = given[pipeline.Id], IdIsStored = true, Attributes = Referring(Typed(pipeline).Attributes, holders) });
            model.AddRange(_read.Where(child => child.Type == "PipelineComponent" && child.ParentId == pipeline.Id)
                .Select(child => Typed(child) with { Id = given[child.Id], IdIsStored = true, ParentId = given[pipeline.Id] }));
        }

        foreach (var type in (string[])["Note", "Annotation", "Accelerator", "Attitude"])
        {
            model.AddRange(_read.Where(element => element.Type == type).Select(element => Typed(element) with { Id = given[element.Id], IdIsStored = true }));
        }

        model.AddRange(_read.Where(element => element.Type == "Evolve").Select(element => Typed(element) with { Id = $"wardley:evolution:{++evolution}", IdIsStored = false, Attributes = Referring(Typed(element).Attributes, holders) }));
        model.AddRange(_read.Where(element => element.Type == "Url").Select(element => Typed(element) with { Id = $"wardley:url:{++url}", IdIsStored = false }));
        return DislModelBuilder.From(new FblModel(model, [], false), WardleyDefinition.Specification, new DislEnv(), "").Diagram;
    }

    private static bool IsPositioned(FblElement element) => element.Type is "Component" or "Anchor" or "Submap";

    /// <summary>A positioned element's attributes: as read and typed, its decorators also as the map writes them, in their order.</summary>
    private static Dictionary<string, object?> Positioned(FblElement element)
    {
        var attributes = new Dictionary<string, object?>(Typed(element).Attributes, StringComparer.Ordinal);
        attributes.TryAdd("url", "");
        attributes.TryAdd("decorators", new List<object?>());
        attributes[WardleyFunctions.WrittenDecorators] = WardleyDecorators.Join(DecoratorsOf(element));
        return attributes;
    }

    /// <summary>A link's attributes: its ends as the first holder of each name, the names as written, and how the menus choose it.</summary>
    private static Dictionary<string, object?> Link(FblElement element, Dictionary<string, string> holders)
    {
        var source = Text(element, "from");
        var target = Text(element, "to");
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["context"] = Text(element, "context"),
            [WardleyFunctions.WrittenSource] = source,
            [WardleyFunctions.WrittenTarget] = target,
            [ChoiceKey] = $"{source}{WardleyIdentityKeys.Separator}{target}{WardleyIdentityKeys.Separator}{KindOf(element)}",
        };
        if (holders.TryGetValue(source, out var from)) attributes["from"] = from;
        if (holders.TryGetValue(target, out var to)) attributes["to"] = to;
        return attributes;
    }

    /// <summary>The attributes of a statement that names an element by its name: its <c>component</c>, the first holder of it, when there is one, and the name as written.</summary>
    private static Dictionary<string, object?> Referring(IReadOnlyDictionary<string, object?> read, Dictionary<string, string> holders)
    {
        var name = read.GetValueOrDefault("component") as string ?? "";
        var attributes = new Dictionary<string, object?>(read, StringComparer.Ordinal) { [WardleyFunctions.WrittenComponent] = name };
        attributes.Remove("component");
        if (holders.TryGetValue(name, out var id)) attributes["component"] = id;
        return attributes;
    }

    /// <summary>
    /// <paramref name="element"/> with every value the definition types given its type: a position's numbers and an
    /// attitude's, a size's, an evolution's and a pipeline component's maturity as numbers, an annotation's number as a whole
    /// number, inertia as a flag, a label offset as its two numbers and an annotation's positions as pairs.
    /// </summary>
    private static FblElement Typed(FblElement element)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in element.Attributes)
        {
            attributes[name] = (name, value) switch
            {
                ("visibility" or "maturity" or "visibility1" or "maturity1" or "visibility2" or "maturity2" or "width" or "height", string text) => Number(text),
                ("number", string text) => (long)Number(text),
                ("inertia", string text) => Inert(text),
                ("labelOffset", string text) => Pair(text) is var (dx, dy) ? new CelMap { ["dx"] = dx, ["dy"] = dy } : null,
                ("positions", IEnumerable<object?> items) => items.OfType<IReadOnlyDictionary<string, object?>>()
                    .Select(object? (item) => new CelMap { ["visibility"] = Number(item.GetValueOrDefault("visibility") as string ?? ""), ["maturity"] = Number(item.GetValueOrDefault("maturity") as string ?? "") })
                    .ToList(),
                _ => value,
            };
        }

        return element with { Attributes = attributes };
    }

    /// <summary>The map of <paramref name="read"/> in the module's own vocabulary.</summary>
    private static WardleyMap Project(List<FblElement> read)
    {
        var components = new List<WardleyComponent>();
        var links = new List<WardleyLink>();
        var pipelines = new List<WardleyPipeline>();
        var evolves = new List<WardleyEvolve>();
        var attitudes = new List<WardleyAttitude>();
        var accelerators = new List<WardleyAccelerator>();
        var notes = new List<WardleyNote>();
        var annotations = new List<WardleyAnnotation>();
        var urls = new List<WardleyUrlDefinition>();
        var title = "";
        var style = "";
        WardleyMapSize? size = null;
        WardleyCoordinate? annotationsPosition = null;

        foreach (var element in read)
        {
            var line = (uint)element.Line;
            switch (element.Type)
            {
                case "Title":
                    title = Text(element, "title");
                    break;
                case "Style":
                    style = Text(element, "style");
                    break;
                case "Size":
                    size = new WardleyMapSize(Number(Text(element, "width")), Number(Text(element, "height")));
                    break;
                case "AnnotationsPosition":
                    annotationsPosition = Coordinate(element, "visibility", "maturity");
                    break;
                case "Component" or "Anchor" or "Submap":
                    components.Add(new WardleyComponent(
                        Text(element, "name"),
                        element.Type switch { "Anchor" => WardleyElementKind.Anchor, "Submap" => WardleyElementKind.Submap, _ => WardleyElementKind.Component },
                        Coordinate(element, "visibility", "maturity"),
                        line,
                        DecoratorsOf(element),
                        Inert(Text(element, "inertia")),
                        Pair(Text(element, "labelOffset")) is var (x, y) ? new WardleyLabelOffset(x, y) : null,
                        Text(element, "url")));
                    break;
                case "Dependency" or "Flow":
                    links.Add(new WardleyLink(Text(element, "from"), Text(element, "to"), KindOf(element), line, Text(element, "context")));
                    break;
                case "Pipeline":
                    pipelines.Add(new WardleyPipeline(
                        Text(element, "component"),
                        [.. read.Where(child => child.Type == "PipelineComponent" && child.ParentId == element.Id).Select(child => new WardleyPipelineChild(Text(child, "name"), Number(Text(child, "maturity")), (uint)child.Line))],
                        line,
                        WardleyPipelineForm.Nested));
                    break;
                case "LegacyPipeline":
                    pipelines.Add(new WardleyPipeline(Text(element, "component"), [], line, WardleyPipelineForm.Legacy, Coordinate(element, "visibility", "maturity")));
                    break;
                case "Evolve":
                    evolves.Add(new WardleyEvolve(Text(element, "component"), Number(Text(element, "maturity")), line, Text(element, "becomes")));
                    break;
                case "Attitude":
                    attitudes.Add(new WardleyAttitude(
                        Text(element, "attitude") switch { "pioneers" => WardleyAttitudeKind.Pioneers, "settlers" => WardleyAttitudeKind.Settlers, _ => WardleyAttitudeKind.TownPlanners },
                        Coordinate(element, "visibility1", "maturity1"),
                        Coordinate(element, "visibility2", "maturity2"),
                        line));
                    break;
                case "Accelerator":
                    accelerators.Add(new WardleyAccelerator(Text(element, "name"), Coordinate(element, "visibility", "maturity"), line, element.Attributes.GetValueOrDefault("deaccelerator") is true));
                    break;
                case "Note":
                    notes.Add(new WardleyNote(Text(element, "text"), Coordinate(element, "visibility", "maturity"), line));
                    break;
                case "Annotation":
                    annotations.Add(new WardleyAnnotation(
                        (int)Number(Text(element, "number")),
                        [.. Typed(element).Attributes.GetValueOrDefault("positions") is IEnumerable<object?> positions
                            ? positions.OfType<CelMap>().Select(position => new WardleyCoordinate((double)position["visibility"]!, (double)position["maturity"]!))
                            : []],
                        Text(element, "text"),
                        line));
                    break;
                case "Url":
                    urls.Add(new WardleyUrlDefinition(Text(element, "name"), Text(element, "address"), line));
                    break;
            }
        }

        return new WardleyMap(title, components, links, pipelines, evolves, attitudes, accelerators, notes, annotations, urls, size, style, annotationsPosition);
    }

    /// <summary>The five decorators <paramref name="element"/> is written with, each once, in the order they are first written; a word that is none of them is kept in the body and read as none.</summary>
    private static List<WardleyDecorator> DecoratorsOf(FblElement element)
    {
        var decorators = new List<WardleyDecorator>();
        if (element.Attributes.GetValueOrDefault("decorators") is not IEnumerable<object?> words) return decorators;
        foreach (var word in words.OfType<string>())
        {
            var decorator = WardleyDecorators.All.Cast<WardleyDecorator?>().FirstOrDefault(candidate => string.Equals(WardleyDecorators.Spell(candidate!.Value), word, StringComparison.OrdinalIgnoreCase));
            if (decorator is { } known && !decorators.Contains(known)) decorators.Add(known);
        }

        return decorators;
    }

    /// <summary>
    /// Whether a component's <c>inertia</c> slot says it has inertia: the binding maps the word to <c>true</c> and reads
    /// <c>false</c> without it, but keeps the word as written in another case (<c>INERTIA</c>), which is inertia too.
    /// </summary>
    private static bool Inert(string text) => text is not ("false" or "");

    private static WardleyLinkKind KindOf(FblElement element) => element.Type == "Flow" ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency;

    private static WardleyCoordinate Coordinate(FblElement element, string visibility, string maturity) =>
        new(Number(Text(element, visibility)), Number(Text(element, maturity)));

    private static string Text(FblElement element, string attribute) =>
        element.Attributes.GetValueOrDefault(attribute) is { } value ? value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    /// <summary>Invariant culture throughout: the format writes <c>0.79</c>, whatever the machine's locale says.</summary>
    private static double Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0d;

    /// <summary>The two numbers of a label offset as the binding reads it, <c>dx, dy</c>; null when it holds no two.</summary>
    private static (double X, double Y)? Pair(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? (x, y)
            : null;
    }

    /// <summary>Which attributes of a binding type hold several values: a positioned element's decorators and an annotation's positions.</summary>
    private static bool ManyOf(string type, string attribute) => attribute is "decorators" or "positions";

    /// <summary>The 1-based line of a byte offset into a body.</summary>
    private sealed class LineStarts(byte[] bytes)
    {
        private readonly int[] _ends = [.. bytes.Select((value, index) => (Value: value, Index: index)).Where(entry => entry.Value == (byte)'\n').Select(entry => entry.Index)];

        public int LineOf(int offset)
        {
            var found = Array.BinarySearch(_ends, offset);
            return (found >= 0 ? found : ~found) + 1;
        }
    }

    /// <summary>The ids the store gives a map's elements, by kind and key; an element it gives none gets one no identity can equal.</summary>
    private sealed class Ids(IReadOnlyList<WardleyIdentityEntry> identities)
    {
        private readonly Dictionary<(string Kind, string Key), string> _ids = identities
            .GroupBy(entry => (entry.Kind, entry.Key))
            .ToDictionary(group => group.Key, group => group.First().Id);

        private int _unidentified;

        public string Of(FblElement element) =>
            _ids.TryGetValue((element.IdentityKind ?? "", element.IdentityKey ?? ""), out var id) ? id : $"wardley:unidentified:{++_unidentified}";
    }

    /// <summary>A few bodies, by their text, the latest first.</summary>
    private sealed class Recent
    {
        private const int Capacity = 8;
        private readonly Lock _lock = new();
        private readonly LinkedList<(string Text, WardleyBody Body)> _bodies = new();

        public WardleyBody Of(string text)
        {
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (node.Value.Text != text) continue;
                    _bodies.Remove(node);
                    _bodies.AddFirst(node);
                    return node.Value.Body;
                }
            }

            var body = new WardleyBody(text);
            lock (_lock)
            {
                _bodies.AddFirst((text, body));
                while (_bodies.Count > Capacity) _bodies.RemoveLast();
            }

            return body;
        }
    }
}
