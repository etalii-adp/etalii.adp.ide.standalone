using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Plans model changes to a pipeline body as splices of FBL 6.1, by byte spans taken from YamlDotNet's marks: the
/// <c>plan</c> of the persistence plugin <see cref="PipelineYamlPlugin"/> (azure-devops-pipeline-disl-fbl task 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only spans, never a reserialization.</b> A value changed is its own span replaced in its own style, a key added
/// goes after the whole value of the key before it, an entry added goes after the last entry of the list itself at
/// the file's own indentation, and an entry removed takes its leading comments and no blank line. Every other byte
/// of the file is left as it was, because the file is Azure DevOps' and a build depends on it.
/// </para>
/// <para>
/// <b>Three keys are written, and whole elements.</b> A <see cref="ModelChange.Set"/> writes <c>displayName</c>,
/// <c>dependsOn</c> and <c>enabled</c> and nothing else (Requirement 5.1); an add, a removal and a move take a whole
/// stage, job or step. An element that is implicit or comes from a template has no bytes here, and a key that
/// arrives through a merge belongs to the anchor, so a change to either is refused with its reason and no splice.
/// </para>
/// <para>
/// <b>Several changes plan as one edit.</b> Every change is planned against the body as it was before any of them, as
/// <see cref="PipelineBody.Batch"/> asks: an add whose parent is an add of the same planner is written inside it, so
/// a new stage is three adds (the stage, its job and the job's step) in one edit.
/// </para>
/// </remarks>
internal sealed class PipelinePlanner
{
    /// <summary>The sentence for a change to an element the reading does not hold.</summary>
    private const string Gone = "That element is no longer in this pipeline.";

    /// <summary>FBL 4.3's reason for a slot reached through a merge key, as the library's declared reader words it.</summary>
    private const string ThroughMerge = "The value is reached through a merge key, so it is read-only.";

    private const string NotWritten = "Only a stage's, a job's or a step's displayName, dependsOn and enabled are written here.";

    private const string NoStages = "This pipeline has no stages block to add a stage to. Add one in the file first.";

    private const string OnOneLine = "That list is written on one line, so an entry is not added to it or taken from it here.";

    private const string DisplayName = "displayName";
    private const string DependsOn = "dependsOn";
    private const string Enabled = "enabled";

    /// <summary>The keys that say what an element is: the kind key a new key goes after when nothing else comes before it.</summary>
    private static readonly HashSet<string> KindKeys = new(StringComparer.Ordinal)
    {
        "stage", "job", "deployment", "task", "script", "bash", "pwsh", "powershell", "checkout", "download", "publish", "template",
    };

    /// <summary>The order the written keys and their neighbours are kept in, after the kind key (finding B8).</summary>
    private static readonly string[] KeyOrder = [DisplayName, DependsOn, "condition", Enabled];

    /// <summary>A deployment job's strategies whose lifecycle hooks hold its steps, in the parser's order.</summary>
    private static readonly string[] Strategies = ["runOnce", "rolling", "canary"];

    private readonly string _text;
    private readonly int[] _byteAt;
    private readonly IReadOnlyList<Line> _lines;
    private readonly int[] _lineStarts;
    private readonly YamlMappingNode? _root;
    private readonly Dictionary<string, FblElement> _elements;
    private readonly IReadOnlyList<FblElement> _reading;
    private readonly Dictionary<long, YamlMappingNode> _entries = [];
    private readonly string _newline;
    private readonly int _sequenceIndent;
    private readonly string? _refusal;

    // What is planned: replacements and removals in the body as it is, and the text inserted at a place in it.
    private readonly List<Replacement> _replacements = [];
    private readonly List<Insertion> _insertions = [];

    // The elements added by this planner, by the id their add gave, and the containers written for them or into
    // elements of the body, so that a second add into one goes beside the first.
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Container> _containers = new(StringComparer.Ordinal);

    private int _order;

    /// <summary>A planner for the body <paramref name="bytes"/>, whose reading is <paramref name="reading"/>.</summary>
    /// <param name="bytes">The body as it is.</param>
    /// <param name="reading">What the plugin read from it, whose ids the changes name.</param>
    /// <param name="refusal">The sentence every change is refused with, for a body nothing is written to; null otherwise.</param>
    public PipelinePlanner(byte[] bytes, IReadOnlyList<FblElement> reading, string? refusal = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(reading);

        _refusal = refusal;
        _text = Encoding.UTF8.GetString(bytes);
        _byteAt = ByteOffsets(_text);
        _lines = LineDocument.Parse(_text).Lines;
        _lineStarts = LineStarts(_lines);
        _reading = reading;
        _elements = reading.GroupBy(element => element.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _newline = _lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length > 0) ?? "\n";

        _root = Load(_text);
        if (_root is not null)
        {
            Index(_root);
        }

        _sequenceIndent = SequenceIndent(_root) ?? 2;
    }

    /// <summary>Everything planned, as splices of the body as it was, in body order.</summary>
    /// <exception cref="InvalidOperationException">Two changes planned together touch the same bytes.</exception>
    public IReadOnlyList<Splice> Splices
    {
        get
        {
            var planned = new List<(int Start, int End, int Kind, int Indent, int Order, SpliceOperation Operation, string Text)>();
            planned.AddRange(_replacements.Select(each => (each.Start, each.End, each.Start == each.End ? 0 : 1, each.Indent, each.Order, each.Operation, each.Text)));
            var unterminated = _text.Length > 0 && _text[^1] != '\n';
            foreach (var insertion in _insertions)
            {
                var pieces = insertion.Piece.Flatten().Select(piece => (piece.Operation, piece.Text)).ToList();
                if (unterminated && insertion.At == _text.Length && pieces.Count > 0)
                {
                    // After the last line of a file with no final newline: the line break goes first, and none is added last.
                    pieces[0] = (pieces[0].Operation, _newline + pieces[0].Text);
                    pieces[^1] = (pieces[^1].Operation, pieces[^1].Text.EndsWith(_newline, StringComparison.Ordinal) ? pieces[^1].Text[..^_newline.Length] : pieces[^1].Text);
                }

                var within = 0;
                foreach ((SpliceOperation operation, string text) in pieces)
                {
                    // The pieces of one insertion keep their order; its place among others at the same offset is its indentation's.
                    planned.Add((insertion.At, insertion.At, 0, insertion.Indent, insertion.Order * 1000 + within++, operation, text));
                }
            }

            // At one offset an insertion comes before what is replaced there, and the deeper text first: it belongs to
            // the element that ends there, and what is shallower comes after that element.
            var ordered = planned
                .OrderBy(each => each.Start)
                .ThenBy(each => each.Kind)
                .ThenByDescending(each => each.Indent)
                .ThenBy(each => each.Order)
                .ToList();
            var splices = new List<Splice>(ordered.Count);
            var end = 0;
            foreach (var each in ordered)
            {
                if (each.Start < end) throw new InvalidOperationException("Two changes planned together touch the same bytes of the pipeline.");
                splices.Add(new Splice(each.Operation, _byteAt[each.Start], _byteAt[each.End], each.Text));
                end = each.End;
            }

            return splices;
        }
    }

    /// <summary>The plan of one change, as the plugin's <c>plan</c> answers it.</summary>
    public static PluginPlanResult PlanOne(byte[] bytes, IReadOnlyList<FblElement> reading, ModelChange change)
    {
        var planner = new PipelinePlanner(bytes, reading);
        return planner.Plan(change) is { } refusal
            ? new PluginPlanResult.Refused(refusal)
            : new PluginPlanResult.Planned([.. planner.Splices.Select(splice => new PluginSplice("", splice))]);
    }

    /// <summary>Plans <paramref name="change"/> beside what is planned already.</summary>
    /// <returns>Null when it is planned; else the sentence it is refused with, and nothing of it is planned.</returns>
    public string? Plan(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_refusal is not null) return _refusal;

        var replacements = _replacements.Count;
        var insertions = _insertions.Count;
        var containers = new Dictionary<string, Container>(_containers, StringComparer.Ordinal);
        var pending = new Dictionary<string, Pending>(_pending, StringComparer.Ordinal);
        var refusal = change switch
        {
            ModelChange.Save => null,
            ModelChange.Set set => Set(set),
            ModelChange.Add add => Add(add),
            ModelChange.Remove remove => Remove(remove),
            ModelChange.Move move => Move(move),
            _ => NotWritten,
        };

        if (refusal is not null)
        {
            // A change is planned whole or not at all.
            _replacements.RemoveRange(replacements, _replacements.Count - replacements);
            _insertions.RemoveRange(insertions, _insertions.Count - insertions);
            Restore(_containers, containers);
            Restore(_pending, pending);
        }

        return refusal;
    }

    private static void Restore<T>(Dictionary<string, T> into, Dictionary<string, T> from)
    {
        into.Clear();
        foreach ((string key, T value) in from)
        {
            into[key] = value;
        }
    }

    // Set: displayName and enabled as scalars, dependsOn in one of its four forms.

    private string? Set(ModelChange.Set set)
    {
        if (Written(set.Id, out var mapping) is { } refused) return refused;

        foreach ((string name, object? value) in set.Attributes.OrderBy(attribute => Array.IndexOf(KeyOrder, attribute.Key)))
        {
            var refusal = name switch
            {
                DisplayName or Enabled => SetScalar(mapping!, name, AsText(value)),
                DependsOn => SetDependsOn(mapping!, value),
                _ => NotWritten,
            };
            if (refusal is not null) return refusal;
        }

        return null;
    }

    /// <summary>A scalar key set to <paramref name="value"/>, or removed when it is empty.</summary>
    private string? SetScalar(YamlMappingNode mapping, string key, string value)
    {
        var own = Own(mapping, key);
        if (own is null && Merged(mapping, key)) return ThroughMerge;

        if (value.Length == 0)
        {
            return own is { } existing ? RemoveKey(mapping, existing.Key, existing.Value) : null;
        }

        if (own is not { } entry)
        {
            return InsertKey(mapping, key, " " + Scalar(value));
        }

        if (entry.Value is YamlScalarNode scalar)
        {
            if (scalar.Value == value) return null;
            ReplaceScalar(scalar, value, ColumnOfKeys(mapping));
            return null;
        }

        Replace(SpliceOperation.ReplaceValue, AfterColon(entry.Key), End(entry.Value), " " + Scalar(value), ColumnOfKeys(mapping));
        return null;
    }

    /// <summary>
    /// <c>dependsOn</c> as the gesture asks: one name as a scalar, several as a block list, none as <c>[]</c>, and null as
    /// the key removed, which is the default order and a different pipeline from waiting for nothing (finding B5).
    /// </summary>
    /// <remarks>
    /// This stands in for language gaps 5 and 6 of azure-devops-pipeline-disl-fbl: FBL has no value that is one scalar
    /// or a sequence of scalars, and no unsetting a slot as distinct from emptying it, so these forms are planned here
    /// and never added to the library.
    /// </remarks>
    private string? SetDependsOn(YamlMappingNode mapping, object? value)
    {
        var own = Own(mapping, DependsOn);
        if (own is null && Merged(mapping, DependsOn)) return ThroughMerge;

        if (value is null)
        {
            return own is { } existing ? RemoveKey(mapping, existing.Key, existing.Value) : null;
        }

        var names = value switch
        {
            string one => new List<string> { one },
            IEnumerable<object?> many => many.Select(AsText).ToList(),
            _ => [AsText(value)],
        };
        var column = ColumnOfKeys(mapping);
        var dash = column + _sequenceIndent;
        var block = string.Concat(names.Select(name => _newline + Spaces(dash) + "- " + Scalar(name)));
        var inline = names.Count == 0 ? "[]" : names.Count == 1 ? Scalar(names[0]) : null;

        if (own is not { } entry)
        {
            return InsertKey(mapping, DependsOn, inline is null ? block : " " + inline);
        }

        switch (entry.Value)
        {
            case YamlScalarNode unchanged when names.Count == 1 && unchanged.Value == names[0]:
                return null;
            case YamlSequenceNode same when names.Count != 1 && Names(same).SequenceEqual(names, StringComparer.Ordinal):
                return null;
            case YamlScalarNode scalar when names.Count == 1:
                ReplaceScalar(scalar, names[0], column);
                return null;
            case YamlScalarNode or YamlSequenceNode { Style: SequenceStyle.Flow } when inline is not null:
                Replace(SpliceOperation.ReplaceValue, Start(entry.Value), End(entry.Value), inline, column);
                return null;
            case YamlSequenceNode { Style: not SequenceStyle.Flow, Children.Count: > 0 } list when inline is null:
                // A block list stays a block list at its own indentation.
                var listColumn = Column(list);
                Replace(SpliceOperation.ReplaceValue, Start(list), End(list), string.Join(_newline + Spaces(listColumn), names.Select(name => "- " + Scalar(name))), column);
                return null;
            default:
                Replace(SpliceOperation.ReplaceValue, AfterColon(entry.Key), End(entry.Value), inline is null ? block : " " + inline, column);
                return null;
        }
    }

    // Add: insert-entry after the last entry of the list itself, at the file's own indentation.

    private string? Add(ModelChange.Add add) => add.Type switch
    {
        "Stage" => AddStage(add),
        "Job" or "DeploymentJob" => AddWithin(add, "Stage", "A job goes in a stage."),
        "Step" => AddWithin(add, "Job", "A step goes in a job."),
        _ => NotWritten,
    };

    private string? AddStage(ModelChange.Add add)
    {
        if (add.ParentId is { } parent && (!_elements.TryGetValue(parent, out var pipeline) || pipeline.Type != "Pipeline")) return "A stage goes in the pipeline.";
        if (_root is null || Own(_root, "stages") is not { Value: YamlSequenceNode stages }) return NoStages;
        if (stages.Style == SequenceStyle.Flow || stages.Children.Count == 0) return OnOneLine;

        var dash = Column(stages);
        return Entry(add, dash) is not { } piece
            ? Unwritable(add)
            : Insert(AfterEntry(stages.Children[^1]), dash, piece);
    }

    /// <summary>A job into a stage, or a step into a job: of the body, or added by this planner.</summary>
    private string? AddWithin(ModelChange.Add add, string parentType, string misplaced)
    {
        if (add.ParentId is { } pendingId && _pending.TryGetValue(pendingId, out var pending))
        {
            if (Family(pending.Type) != parentType) return misplaced;

            // Into an element of this planner: the keys on the way are written into its text, once.
            string[] path = add.Type != "Step" ? ["jobs"] : pending.Type != "DeploymentJob" ? ["steps"] : pending.Strategy is null ? StepsPath(add) : StepsPath(add)[2..];
            var column = pending.Type == "DeploymentJob" && add.Type == "Step" && pending.Strategy is not null ? pending.KeyColumn + 4 : pending.KeyColumn;
            var name = string.Join('/', path);
            var into = pending.Containers.TryGetValue(name, out var known) ? known : NewContainer(column, path);
            if (Entry(add, into.Dash) is not { } written) return Unwritable(add);
            if (known is null)
            {
                pending.Piece.Inside.Add(into.Piece!);
                pending.Containers[name] = into;
            }

            into.Piece!.Inside.Add(written);
            return null;
        }

        if (Written(add.ParentId ?? "", out var parent) is { } refused) return refused;
        var element = _elements[add.ParentId!];
        if (Family(element.Type) != parentType) return misplaced;

        string[] steps = add.Type != "Step" ? ["jobs"] : element.Type == "DeploymentJob" && Own(parent!, "steps") is null ? StepsPath(add) : ["steps"];
        if (ListOf(parent!, steps) is not { } list) return OnOneLine;
        return Entry(add, list.Dash) is { } entry ? Into(list, entry) : Unwritable(add);
    }

    /// <summary>Where a step of <paramref name="add"/> goes in a job: its <c>steps</c>, or those of the lifecycle hook it names.</summary>
    private static string[] StepsPath(ModelChange.Add add) =>
        ["strategy", "runOnce", AsText(add.Attributes.GetValueOrDefault("hook")) is { Length: > 0 } hook ? hook : "deploy", "steps"];

    /// <summary>The entry text of a new element at <paramref name="dash"/>, written from its attributes in the keys' order; null for an attribute that is not written.</summary>
    private Piece? Entry(ModelChange.Add add, int dash)
    {
        var known = add.Type switch
        {
            "Step" => new HashSet<string>(["stepKind", "identifier", DisplayName, "condition", Enabled, "hook"], StringComparer.Ordinal),
            "DeploymentJob" => new HashSet<string>(["name", DisplayName, DependsOn, "environment", "strategy", Enabled], StringComparer.Ordinal),
            _ => new HashSet<string>(["name", DisplayName, DependsOn, Enabled], StringComparer.Ordinal),
        };
        if (add.Attributes.Keys.Any(key => !known.Contains(key))) return null;

        var column = dash + 2;
        var kind = add.Type switch
        {
            "Stage" => ("stage", AsText(add.Attributes.GetValueOrDefault("name"))),
            "Job" => ("job", AsText(add.Attributes.GetValueOrDefault("name"))),
            "DeploymentJob" => ("deployment", AsText(add.Attributes.GetValueOrDefault("name"))),
            _ => (AsText(add.Attributes.GetValueOrDefault("stepKind")) is { Length: > 0 } stepKind ? stepKind : "script", AsText(add.Attributes.GetValueOrDefault("identifier"))),
        };
        if (kind.Item2.Length == 0) return null;

        var text = new StringBuilder(Spaces(dash)).Append("- ").Append(kind.Item1).Append(": ").Append(Scalar(kind.Item2)).Append(_newline);
        if (AsText(add.Attributes.GetValueOrDefault("environment")) is { Length: > 0 } environment)
        {
            text.Append(Spaces(column)).Append("environment: ").Append(Scalar(environment)).Append(_newline);
        }

        if (AsText(add.Attributes.GetValueOrDefault(DisplayName)) is { Length: > 0 } displayName)
        {
            text.Append(Spaces(column)).Append("displayName: ").Append(Scalar(displayName)).Append(_newline);
        }

        if (add.Attributes.GetValueOrDefault(DependsOn) is IEnumerable<object?> dependsOn)
        {
            var names = dependsOn.Select(AsText).ToList();
            text.Append(Spaces(column)).Append("dependsOn:").Append(names.Count switch
            {
                0 => " []",
                1 => " " + Scalar(names[0]),
                _ => string.Concat(names.Select(name => _newline + Spaces(column + _sequenceIndent) + "- " + Scalar(name))),
            }).Append(_newline);
        }

        if (AsText(add.Attributes.GetValueOrDefault("condition")) is { Length: > 0 } condition)
        {
            text.Append(Spaces(column)).Append("condition: ").Append(Scalar(condition)).Append(_newline);
        }

        if (AsText(add.Attributes.GetValueOrDefault(Enabled)) is { Length: > 0 } enabled)
        {
            text.Append(Spaces(column)).Append("enabled: ").Append(Scalar(enabled)).Append(_newline);
        }

        // A deployment job's strategy comes last, because its hooks, and the steps in them, are written into it.
        if (AsText(add.Attributes.GetValueOrDefault("strategy")) is { Length: > 0 } strategy)
        {
            text.Append(Spaces(column)).Append("strategy:").Append(_newline).Append(Spaces(column + 2)).Append(strategy).Append(':').Append(_newline);
        }

        var piece = new Piece(SpliceOperation.InsertEntry, text.ToString());
        if (add.Id is { } id)
        {
            _pending[id] = new Pending(add.Type, column, piece, AsText(add.Attributes.GetValueOrDefault("strategy")) is { Length: > 0 } written ? written : null);
        }

        return piece;
    }

    private static string Unwritable(ModelChange.Add add) =>
        $"A new {add.Type} is written with its name or its kind and only the keys this tool writes.";

    /// <summary>An entry into a list, of the body or written by this planner.</summary>
    private string? Into(Container list, Piece entry)
    {
        if (list.Piece is { } container)
        {
            container.Inside.Add(entry);
            return null;
        }

        return Insert(list.At, list.Dash, entry);
    }

    private string? Insert(int at, int indent, Piece piece)
    {
        _insertions.Add(new Insertion(at, indent, piece, _order++));
        return null;
    }

    /// <summary>
    /// The list at <paramref name="path"/> under <paramref name="mapping"/>, written as a block list: where an entry
    /// goes and at what column. The keys on the way that it does not have are written, once, as one container.
    /// </summary>
    private Container? ListOf(YamlMappingNode mapping, string[] path)
    {
        var cacheKey = $"{Start(mapping)}/{string.Join('/', path)}";
        if (_containers.TryGetValue(cacheKey, out var known)) return known;

        var current = mapping;
        for (var index = 0; index < path.Length; index++)
        {
            var key = path[index];
            if (index == 1 && path[0] == "strategy")
            {
                // A deployment job's strategy is whichever of runOnce, rolling and canary it writes.
                key = Strategies.FirstOrDefault(strategy => Own(current, strategy) is not null) ?? key;
            }

            var own = Own(current, key);
            if (own is null)
            {
                var container = NewContainer(ColumnOfKeys(current), path[index..]);
                _insertions.Add(new Insertion(AfterEntry(current), ColumnOfKeys(current), container.Piece!, _order++));
                return _containers[cacheKey] = container;
            }

            var last = index == path.Length - 1;
            switch (own.Value.Value)
            {
                case YamlSequenceNode { Style: not SequenceStyle.Flow, Children.Count: > 0 } list when last:
                    return _containers[cacheKey] = new Container(AfterEntry(list.Children[^1]), Column(list), null);
                case YamlMappingNode next when !last:
                    current = next;
                    continue;
                case YamlScalarNode { Value: null or "" }:
                    // A key with nothing under it yet: what follows is written on the lines after it.
                    var under = NewContainer(Column(own.Value.Key) + 2, path[(index + 1)..], Column(own.Value.Key) + _sequenceIndent);
                    var at = AfterEntry(own.Value.Key);
                    if (under.Piece is { } piece)
                    {
                        _insertions.Add(new Insertion(at, Column(own.Value.Key) + 2, piece, _order++));
                        return _containers[cacheKey] = under;
                    }

                    return _containers[cacheKey] = new Container(at, under.Dash, null);
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The keys <paramref name="keys"/> written as a chain from <paramref name="column"/>, the last holding the list; none when there are none.</summary>
    private Container NewContainer(int column, string[] keys, int? dash = null)
    {
        if (keys.Length == 0) return new Container(0, dash ?? column, null);

        var text = new StringBuilder();
        for (var index = 0; index < keys.Length; index++)
        {
            text.Append(Spaces(column + index * 2)).Append(keys[index]).Append(':').Append(_newline);
        }

        return new Container(0, column + (keys.Length - 1) * 2 + _sequenceIndent, new Piece(SpliceOperation.EnsureContainer, text.ToString()));
    }

    // Remove: remove-entry, the entry and its leading comments and no blank line.

    private string? Remove(ModelChange.Remove remove)
    {
        if (Written(remove.Id, out var mapping) is { } refused) return refused;
        if (Removal(mapping!) is not { } span) return OnOneLine;

        Replace(SpliceOperation.RemoveEntry, span.Start, span.End, "", Column(mapping!));
        return null;
    }

    // Move: remove-entry and insert-entry of the same bytes, in one edit (Requirement 5.6).

    private string? Move(ModelChange.Move move)
    {
        if (Written(move.Id, out var mapping) is { } refused) return refused;
        var element = _elements[move.Id];
        if (move.NewParentId != element.ParentId) return "An element moves among the elements beside it here, not to another parent.";
        if (Removal(mapping!) is not { } span) return OnOneLine;

        var family = Family(element.Type);
        var siblings = _reading.Where(each => !each.IsRelation && each.ParentId == element.ParentId && Family(each.Type) == family).ToList();
        var target = move.Index >= 0 && move.Index < siblings.Count ? siblings[move.Index] : null;
        var own = siblings.FindIndex(each => each.Id == element.Id);
        if (target is null ? own == siblings.Count - 1 : move.Index == own || move.Index == own + 1) return null;

        int at;
        if (target is null)
        {
            var last = siblings.LastOrDefault(each => each.Line > 0 && _entries.ContainsKey(each.Line));
            if (last is null) return Gone;
            at = AfterEntry(_entries[last.Line]);
        }
        else
        {
            if (Written(target.Id, out var before) is { } elsewhere) return elsewhere;
            if (Removal(before!) is not { } place) return OnOneLine;
            at = place.Start;
        }

        if (at >= span.Start && at <= span.End) return null;
        if (Indentation(LineOf(at < _text.Length ? at : at - 1)) != Indentation(LineOf(Start(mapping!))) && target is not null)
        {
            return "That place is at another indentation, so the element is not moved there.";
        }

        var bytes = _text[span.Start..span.End];
        if (!bytes.EndsWith('\n'))
        {
            // The entry is the last of a file with no final newline: it is moved with one, and leaves none behind.
            bytes = bytes.TrimStart('\r', '\n') + _newline;
        }

        if (at == _text.Length && _text.Length > 0 && _text[^1] != '\n')
        {
            bytes = _newline + bytes[..^_newline.Length];
        }

        Replace(SpliceOperation.RemoveEntry, span.Start, span.End, "", Column(mapping!));
        _replacements.Add(new Replacement(SpliceOperation.InsertEntry, at, at, bytes, Column(mapping!), _order++));
        return null;
    }

    /// <summary>The kind of element a move reorders among: stages with stages, jobs with jobs, steps with steps.</summary>
    private static string Family(string type) => type switch
    {
        "Stage" or "ImplicitStage" => "Stage",
        "Job" or "DeploymentJob" or "ImplicitJob" => "Job",
        _ => type,
    };

    // What every change finds first.

    /// <summary>
    /// The entry of the element <paramref name="id"/> names, or the sentence a change to it is refused with: it is not
    /// in the reading, it is implicit or comes from a template, or it is not a stage, a job or a step.
    /// </summary>
    private string? Written(string id, out YamlMappingNode? mapping)
    {
        mapping = null;
        if (!_elements.TryGetValue(id, out var element)) return Gone;
        if (element.IsRelation || Family(element.Type) is not ("Stage" or "Job" or "Step")) return NotWritten;

        var isImplicit = element.Attributes.GetValueOrDefault("isImplicit") is true;
        var template = AsText(element.Attributes.GetValueOrDefault("template"));
        var target = new PipelineEditTarget(element.Id, default, isImplicit, template);
        if (!target.IsEditable) return target.ReadOnlyReason;

        return _entries.TryGetValue(element.Line, out mapping) ? null : Gone;
    }

    /// <summary>The span an entry and its leading comments take, as whole lines; null for an entry that does not start its line.</summary>
    private (int Start, int End)? Removal(YamlMappingNode entry)
    {
        var range = YamlNodeRange.Of(entry, _lines);
        if (!_lines[range.Start].Text.TrimStart(' ').StartsWith('-')) return null;

        var first = range.Start;
        while (first > 0 && _lines[first - 1].Text.TrimStart(' ').StartsWith('#'))
        {
            first--;
        }

        var end = _lineStarts[range.End + 1];
        if (range.End == _lines.Count - 1 && _lines[range.End].Ending.Length == 0 && first > 0)
        {
            // The last line of a file with no final newline: the line break before it goes, so none is left dangling.
            return (_lineStarts[first] - _lines[first - 1].Ending.Length, end);
        }

        return (_lineStarts[first], end);
    }

    // The three splices of a key.

    /// <summary>A key added after the whole value of the key before it in <see cref="KeyOrder"/>, never inside a block scalar (finding B8).</summary>
    private string? InsertKey(YamlMappingNode mapping, string key, string value)
    {
        var position = Array.IndexOf(KeyOrder, key);
        KeyValuePair<YamlNode, YamlNode>? after = null;
        var best = int.MinValue;
        foreach (var child in mapping.Children)
        {
            if (child.Key is not YamlScalarNode { Value: { } name }) continue;
            after ??= child;
            var rank = KindKeys.Contains(name) ? -1 : Array.IndexOf(KeyOrder, name);
            if ((rank == -1 && !KindKeys.Contains(name)) || rank >= position || rank <= best) continue;
            best = rank;
            after = child;
        }

        if (after is not { } before) return NotWritten;

        var column = ColumnOfKeys(mapping);
        var at = AfterEntry(before.Key, before.Value);
        var text = Spaces(column) + key + ":" + value + _newline;
        _insertions.Add(new Insertion(at, column, new Piece(SpliceOperation.InsertKey, text), _order++));
        return null;
    }

    /// <summary>A key removed with its whole value: its lines, or up to the next key when it shares its line with the entry's dash.</summary>
    private string? RemoveKey(YamlMappingNode mapping, YamlNode key, YamlNode value)
    {
        var column = ColumnOfKeys(mapping);
        if (Start(key) == Start(mapping))
        {
            var next = mapping.Children.Keys.Skip(1).FirstOrDefault();
            if (next is null) return NotWritten;
            Replace(SpliceOperation.RemoveKey, Start(key), Start(next), "", column);
            return null;
        }

        var range = YamlNodeRange.Of(key, value, _lines);
        var start = _lineStarts[range.Start];
        var end = _lineStarts[range.End + 1];
        if (range.End == _lines.Count - 1 && _lines[range.End].Ending.Length == 0 && range.Start > 0)
        {
            start -= _lines[range.Start - 1].Ending.Length;
        }

        Replace(SpliceOperation.RemoveKey, start, end, "", column);
        return null;
    }

    /// <summary>A scalar's own span replaced by <paramref name="value"/> in the scalar's own style; an expression as written.</summary>
    private void ReplaceScalar(YamlScalarNode scalar, string value, int column)
    {
        var start = Start(scalar);
        if (scalar.Style is ScalarStyle.Literal or ScalarStyle.Folded && !IsExpression(value))
        {
            // A block scalar keeps its header and the indentation of its content.
            var range = YamlNodeRange.Of(scalar, _lines);
            var end = _lineStarts[range.End] + _lines[range.End].Text.Length;
            var header = _lines[LineOf(start)].Text[(start - _lineStarts[LineOf(start)])..].TrimEnd();
            var content = range.End > range.Start ? Indentation(range.Start + 1) : column + 2;
            var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var separator = scalar.Style == ScalarStyle.Folded ? _newline + _newline : _newline;
            var text = header + _newline + string.Join(separator, lines.Select(line => line.Length == 0 ? "" : Spaces(content) + line));
            Replace(SpliceOperation.ReplaceValue, start, end, text, column);
            return;
        }

        var written = scalar.Style switch
        {
            _ when IsExpression(value) => value,
            ScalarStyle.SingleQuoted when !value.Contains('\n') => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'",
            ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted => DoubleQuoted(value),
            _ => Scalar(value),
        };
        Replace(SpliceOperation.ReplaceValue, start, End(scalar), written, column);
    }

    private void Replace(SpliceOperation operation, int start, int end, string text, int indent) =>
        _replacements.Add(new Replacement(operation, start, end, text, indent, _order++));

    // Spans, in chars of the text; converted to bytes only when the splices are handed out.

    /// <summary>Where <paramref name="node"/> starts.</summary>
    private int Start(YamlNode node) => (int)Math.Min(node.Start.Index, _text.Length);

    /// <summary>
    /// Where the value <paramref name="node"/> ends: after a flow node's last character, and at the end of the last
    /// line that says something of a block node, before its line break.
    /// </summary>
    private int End(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode { Style: not (ScalarStyle.Literal or ScalarStyle.Folded) }:
                return (int)Math.Min(node.End.Index, _text.Length);
            case YamlSequenceNode { Style: SequenceStyle.Flow }:
            case YamlMappingNode { Style: MappingStyle.Flow }:
                return FlowEnd(Start(node));
            case YamlSequenceNode { Children.Count: > 0 } list:
                return End(list.Children[^1]);
            case YamlMappingNode { Children.Count: > 0 } map:
                return End(map.Children[^1].Value);
            default:
                var range = YamlNodeRange.Of(node, _lines);
                return _lineStarts[range.End] + _lines[range.End].Text.Length;
        }
    }

    /// <summary>
    /// Just after the bracket that closes the flow collection opening at <paramref name="start"/>, read from the text past
    /// quoted scalars and nested collections: YamlDotNet's end mark is not that bracket (an empty <c>[]</c> spans nothing).
    /// </summary>
    private int FlowEnd(int start)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = start; index < _text.Length; index++)
        {
            var c = _text[index];
            if (quote != '\0')
            {
                if (c == '\\' && quote == '"') index++;
                else if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '[' or '{':
                    depth++;
                    break;
                case ']' or '}':
                    if (--depth == 0) return index + 1;
                    break;
            }
        }

        return _text.Length;
    }

    /// <summary>The start of the line after the whole of <paramref name="node"/>: where what follows it is written.</summary>
    private int AfterEntry(YamlNode node) => _lineStarts[YamlNodeRange.Of(node, _lines).End + 1];

    /// <summary>The start of the line after the whole value of a key.</summary>
    private int AfterEntry(YamlNode key, YamlNode value) => _lineStarts[YamlNodeRange.Of(key, value, _lines).End + 1];

    /// <summary>Just after the colon that follows <paramref name="key"/>.</summary>
    private int AfterColon(YamlNode key)
    {
        var colon = _text.IndexOf(':', (int)key.End.Index);
        return colon < 0 ? (int)key.End.Index : colon + 1;
    }

    private static int Column(YamlNode node) => (int)node.Start.Column - 1;

    /// <summary>The column a mapping's keys are written at: its first key's.</summary>
    private static int ColumnOfKeys(YamlMappingNode mapping) => Column(mapping);

    private int Indentation(int line) => _lines[line].Text.Length - _lines[line].Text.TrimStart(' ').Length;

    private int LineOf(int at)
    {
        var line = Array.BinarySearch(_lineStarts, at);
        return Math.Clamp(line >= 0 ? line : ~line - 1, 0, Math.Max(0, _lines.Count - 1));
    }

    // Keys: an element's own, and the ones a merge brings.

    private static KeyValuePair<YamlNode, YamlNode>? Own(YamlMappingNode mapping, string key)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode { Value: { } name } && name == key) return child;
        }

        return null;
    }

    /// <summary>Whether a merge key of <paramref name="mapping"/> brings <paramref name="key"/> (finding B13).</summary>
    private static bool Merged(YamlMappingNode mapping, string key, int depth = 0)
    {
        if (depth > 16 || Own(mapping, "<<") is not { } merge) return false;
        IEnumerable<YamlNode> sources = merge.Value is YamlSequenceNode sequence ? sequence.Children : [merge.Value];
        return sources.OfType<YamlMappingNode>().Any(source => Own(source, key) is not null || Merged(source, key, depth + 1));
    }

    private static IEnumerable<string> Names(YamlSequenceNode list) =>
        list.Children.Select(child => child is YamlScalarNode { Value: { } name } ? name : "\0");

    // Scalars as written: every value is the text it was written with (finding B1).

    /// <summary>A value as a plain scalar where YAML's syntax allows it, else double-quoted; an expression as written.</summary>
    private static string Scalar(string value) =>
        IsExpression(value) || IsPlainSyntax(value) ? value : DoubleQuoted(value);

    /// <summary>A compile-time or runtime expression, which is written back as written and never quoted.</summary>
    private static bool IsExpression(string value) =>
        value.StartsWith("${{", StringComparison.Ordinal) || value.StartsWith("$[", StringComparison.Ordinal);

    /// <summary>
    /// Whether YAML's syntax lets <paramref name="value"/> be written plain, whatever a schema would type it as: this
    /// tool reads every value as the text it was written with, so <c>true</c> and <c>1.0</c> stay bare.
    /// </summary>
    private static bool IsPlainSyntax(string value)
    {
        if (value.Length == 0) return false;
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return false;
        if (value.Any(c => c is '\n' or '\r' || char.IsControl(c))) return false;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0])) return false;
        return !value.Contains(": ", StringComparison.Ordinal) && !value.Contains(" #", StringComparison.Ordinal) && !value.EndsWith(':');
    }

    private static string DoubleQuoted(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static string AsText(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Spaces(int count) => new(' ', Math.Max(0, count));

    // Reading the body once, for its marks.

    private static YamlMappingNode? Load(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception error) when (error is YamlException or InvalidOperationException or ArgumentException)
        {
            // The plugin reads such a body as unreadable, and the library never asks to plan for it.
            return null;
        }
    }

    /// <summary>Every mapping that is an entry of a list, by the line it starts on, as the plugin reports an element's line.</summary>
    private void Index(YamlNode node)
    {
        switch (node)
        {
            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                {
                    if (child is YamlMappingNode entry) _entries.TryAdd(entry.Start.Line, entry);
                    Index(child);
                }

                break;
            case YamlMappingNode mapping:
                foreach (var child in mapping.Children)
                {
                    Index(child.Value);
                }

                break;
        }
    }

    /// <summary>How far the file indents a list under its key: the first block list that is a key's value says; null when none does.</summary>
    private static int? SequenceIndent(YamlNode? node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var child in mapping.Children)
                {
                    if (child.Value is YamlSequenceNode { Style: not SequenceStyle.Flow, Children.Count: > 0 } list && child.Key.Start.Line < list.Start.Line)
                    {
                        return Math.Max(0, Column(list) - Column(child.Key));
                    }

                    if (SequenceIndent(child.Value) is { } found) return found;
                }

                return null;
            case YamlSequenceNode sequence:
                return sequence.Children.Select(SequenceIndent).FirstOrDefault(found => found is not null);
            default:
                return null;
        }
    }

    private static int[] LineStarts(IReadOnlyList<Line> lines)
    {
        var starts = new int[lines.Count + 1];
        for (var index = 0; index < lines.Count; index++)
        {
            starts[index + 1] = starts[index] + lines[index].Text.Length + lines[index].Ending.Length;
        }

        return starts;
    }

    /// <summary>The UTF-8 byte offset of every char offset of <paramref name="text"/>, its end included.</summary>
    private static int[] ByteOffsets(string text)
    {
        var offsets = new int[text.Length + 1];
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                offsets[index + 1] = offsets[index] + 2;
                offsets[index + 2] = offsets[index] + 4;
                index++;
                continue;
            }

            offsets[index + 1] = offsets[index] + (c < 0x80 ? 1 : c < 0x800 ? 2 : 3);
        }

        return offsets;
    }

    /// <summary>A replacement or removal of the text from <see cref="Start"/> to <see cref="End"/>, or a move's insertion.</summary>
    private sealed record Replacement(SpliceOperation Operation, int Start, int End, string Text, int Indent, int Order);

    /// <summary>Text inserted at <see cref="At"/>, with what is written into it.</summary>
    private sealed record Insertion(int At, int Indent, Piece Piece, int Order);

    /// <summary>One splice of inserted text, and the containers and entries written into it, in document order.</summary>
    private sealed class Piece(SpliceOperation operation, string text)
    {
        public SpliceOperation Operation { get; } = operation;

        public string Text { get; } = text;

        public List<Piece> Inside { get; } = [];

        public IEnumerable<Piece> Flatten() => Inside.SelectMany(piece => piece.Flatten()).Prepend(this);
    }

    /// <summary>An element this planner adds: its type, the column of its keys, its text, and the containers written into it.</summary>
    private sealed record Pending(string Type, int KeyColumn, Piece Piece, string? Strategy)
    {
        public Dictionary<string, Container> Containers { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A list an entry goes into: where and at what column, and the container written for it, when it is new.</summary>
    private sealed record Container(int At, int Dash, Piece? Piece);
}
