using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>agent-behavior-modelling.fbl#abm</c>
/// (<c>net.etalii.adp.etalii.abmMarkdown</c>): it reads the tree with <see cref="AbmParser"/> and plans
/// every change with <see cref="AbmWriter"/>, so the Markdown is written exactly as the module has
/// always written it, and every byte outside the lines a change touches survives.
/// </summary>
/// <remarks>
/// <para>
/// <b>A node is an element of the DISL type <c>x-abm.types</c> maps to its kind</b>, with
/// <c>label</c>, <c>notes</c>, a Retry's <c>attempts</c> and a Do's <c>implicit</c> (an item without a
/// keyword). Its id is its place, <c>1.2.1</c>, which is not stored: the definition derives it.
/// </para>
/// <para>
/// <b>It keeps no state</b> (FBL §11.4): <see cref="Plan"/> parses the bytes it is handed again, makes the
/// change on a copy of their lines, and hands back the bytes that differ as one splice - or two for a
/// move, the subtree taken out and put in again. An edit the writer refuses comes back refused, in its words.
/// </para>
/// <para>
/// <b>Its one finding is <c>abm.no-behavior</c></b>, information that the file has no Behavior heading
/// (line 1) or that the section holds no list yet (the heading's line). Every other finding is the
/// definition's: an item without a keyword is read with <c>implicit</c>, and the definition says so.
/// </para>
/// </remarks>
public sealed class AbmMarkdownPlugin : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    private const string PluginId = "net.etalii.adp.etalii.abmMarkdown";

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = Body(request.Files);
        var document = LineDocument.Parse(Encoding.UTF8.GetString(bytes));
        var model = AbmParser.Parse(document);
        var starts = LineStarts(bytes);

        var elements = model.Nodes.Select(node => ElementOf(node, starts, bytes.Length)).ToList();
        return new PluginReadResult(elements, Findings(model), false);
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var before = Body(request.Files);
        var document = LineDocument.Parse(Encoding.UTF8.GetString(before));
        var model = AbmParser.Parse(document);

        var edit = Apply(document, model, request.Change);
        if (!edit.WasApplied) return new PluginPlanResult.Refused(edit.Refusal!);

        var after = Encoding.UTF8.GetBytes(document.Text);
        if (request.Change is ModelChange.Move move && model.NodeOf(move.Id) is { } moved
            && MoveSplices(before, after, model, moved, move) is { } splices)
        {
            return new PluginPlanResult.Planned(splices);
        }

        return new PluginPlanResult.Planned(Difference(before, after, OperationOf(request.Change)) is { } splice ? [new PluginSplice("", splice)] : []);
    }

    /// <inheritdoc />
    public byte[] Template(PluginTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encoding.UTF8.GetBytes(AbmDocumentFactory.EmptyDocument(request.Name, "\r\n"));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    /// <summary>The kind of a node type, or null for a type that is not one of the eleven.</summary>
    private static string? KindOf(string type) => AbmDefinition.KindOfType.GetValueOrDefault(type);

    /// <summary>The change made on <paramref name="document"/>, as the module's commands make it.</summary>
    private static AbmEdit Apply(LineDocument document, AbmModel model, ModelChange change)
    {
        switch (change)
        {
            case ModelChange.Set set:
            {
                if (model.NodeOf(set.Id) is null) return Gone();
                foreach ((string name, object? value) in set.Attributes)
                {
                    // Each attribute is written on the lines as they are after the one before.
                    var node = AbmParser.Parse(document).NodeOf(set.Id)!;
                    var edit = name switch
                    {
                        "label" => AbmWriter.SetLabel(document, node, Text(value)),
                        "notes" => AbmWriter.SetNotes(document, node, Text(value)),
                        "attempts" when node.Kind != AbmNodeKinds.Retry => AbmEdit.Refused("Only a Retry has attempts."),
                        "attempts" when Number(value) is null => AbmEdit.Refused("A Retry's attempts are a whole number."),
                        "attempts" => AbmWriter.SetKind(document, node, AbmNodeKinds.Retry, Number(value)!.Value),
                        _ => AbmEdit.Refused($"A behavior model node has no `{name}` to write."),
                    };
                    if (!edit.WasApplied) return edit;
                }
                return AbmEdit.Applied;
            }
            case ModelChange.Add add:
            {
                if (KindOf(add.Type) is not { } kind) return AbmEdit.Refused($"A behavior model has no `{add.Type}` node.");
                AbmNode? parent = null;
                if (add.ParentId is { } parentId && (parent = model.NodeOf(parentId)) is null) return Gone();
                var attempts = add.Attributes.TryGetValue("attempts", out var count) ? Number(count) ?? 0 : 0;
                (AbmEdit edit, string id) = AbmWriter.Add(document, model, parent, add.Index ?? -1, kind, Text(add.Attributes.GetValueOrDefault("label")), attempts);
                if (!edit.WasApplied) return edit;
                var notes = Text(add.Attributes.GetValueOrDefault("notes"));
                return notes.Length == 0 ? edit : AbmWriter.SetNotes(document, AbmParser.Parse(document).NodeOf(id)!, notes);
            }
            case ModelChange.Remove remove:
                return model.NodeOf(remove.Id) is { } removed ? AbmWriter.Remove(document, removed) : Gone();
            case ModelChange.Move move:
            {
                if (model.NodeOf(move.Id) is not { } node) return Gone();
                AbmNode? parent = null;
                if (move.NewParentId is { } parentId && (parent = model.NodeOf(parentId)) is null)
                {
                    return AbmEdit.Refused("The node it was moved under is no longer in this behavior model.");
                }
                return AbmWriter.Move(document, model, node, parent, move.Index);
            }
            case ModelChange.Retype retype:
            {
                if (model.NodeOf(retype.Id) is not { } node) return Gone();
                if (KindOf(retype.Type) is not { } kind) return AbmEdit.Refused($"A behavior model has no `{retype.Type}` node.");
                var attempts = retype.Attributes.TryGetValue("attempts", out var count) ? Number(count) ?? 0 : 0;
                return AbmWriter.SetKind(document, node, kind, attempts);
            }
            case ModelChange.Save:
                return AbmEdit.Applied;
            default:
                return AbmEdit.Refused("A behavior model cannot make that change.");
        }
    }

    private static AbmEdit Gone() => AbmEdit.Refused("That node is no longer in this behavior model.");

    private static SpliceOperation OperationOf(ModelChange change) => change switch
    {
        ModelChange.Set { Attributes.Count: 1 } set when set.Attributes.ContainsKey("notes") => SpliceOperation.ReplaceValue,
        ModelChange.Add => SpliceOperation.InsertEntry,
        ModelChange.Remove => SpliceOperation.RemoveEntry,
        _ => SpliceOperation.ReEmitLine,
    };

    /// <summary>
    /// A move as what it is: the subtree's bytes taken out, and its re-indented lines put in where it
    /// lands. Null when the writer's bytes are not that, which the one-splice difference then covers.
    /// </summary>
    private static IReadOnlyList<PluginSplice>? MoveSplices(byte[] before, byte[] after, AbmModel model, AbmNode node, ModelChange.Move move)
    {
        var starts = LineStarts(before);
        var removedStart = Start(starts, node.Line, before.Length);
        var removedEnd = Start(starts, node.SubtreeEnd + 1, before.Length);
        var parent = move.NewParentId is { } parentId ? model.NodeOf(parentId) : null;
        var siblings = parent is null ? model.Roots : model.ChildrenOf(parent);
        var at = Math.Clamp(move.Index < 0 ? siblings.Count : move.Index, 0, siblings.Count);
        var targetLine = at < siblings.Count ? siblings[at].Line : (parent?.SubtreeEnd ?? model.Roots[^1].SubtreeEnd) + 1;
        var target = Start(starts, targetLine, before.Length);
        if (target > removedStart && target < removedEnd) return null;

        var inserted = after.Length - (before.Length - (removedEnd - removedStart));
        if (inserted < 0) return null;

        // The bytes the move must give, laid out from the old ones; anything else is not this move.
        int insertAt;
        if (target >= removedEnd)
        {
            insertAt = removedStart + (target - removedEnd);
            if (!Same(before, 0, after, 0, removedStart) || !Same(before, removedEnd, after, removedStart, target - removedEnd)
                || !Same(before, target, after, insertAt + inserted, before.Length - target)) return null;
        }
        else
        {
            insertAt = target;
            if (!Same(before, 0, after, 0, target) || !Same(before, target, after, target + inserted, removedStart - target)
                || !Same(before, removedEnd, after, removedStart + inserted, before.Length - removedEnd)) return null;
        }

        var text = Encoding.UTF8.GetString(after, insertAt, inserted);
        if (Encoding.UTF8.GetByteCount(text) != inserted) return null;
        var removal = new PluginSplice("", new Splice(SpliceOperation.RemoveEntry, removedStart, removedEnd, ""));
        var insertion = new PluginSplice("", new Splice(SpliceOperation.InsertEntry, target, target, text));

        // In body order: an insertion where the subtree starts goes before its removal.
        return target <= removedStart ? [insertion, removal] : [removal, insertion];
    }

    /// <summary>
    /// The one splice that turns <paramref name="before"/> into <paramref name="after"/>: the bytes between
    /// their common start and their common end, widened to whole characters. Null when nothing differs.
    /// </summary>
    private static Splice? Difference(byte[] before, byte[] after, SpliceOperation operation)
    {
        var prefix = 0;
        var limit = Math.Min(before.Length, after.Length);
        while (prefix < limit && before[prefix] == after[prefix]) prefix++;
        if (prefix == before.Length && prefix == after.Length) return null;

        var suffix = 0;
        while (suffix < limit - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        // A UTF-8 continuation byte is never where a character starts.
        while (prefix > 0 && IsContinuation(after, prefix)) prefix--;
        while (suffix > 0 && IsContinuation(after, after.Length - suffix)) suffix--;

        return new Splice(operation, prefix, before.Length - suffix, Encoding.UTF8.GetString(after, prefix, after.Length - suffix - prefix));
    }

    private static bool IsContinuation(byte[] bytes, int index) => index < bytes.Length && (bytes[index] & 0xC0) == 0x80;

    private static bool Same(byte[] left, int leftStart, byte[] right, int rightStart, int count) =>
        count >= 0 && leftStart + count <= left.Length && rightStart + count <= right.Length
        && left.AsSpan(leftStart, count).SequenceEqual(right.AsSpan(rightStart, count));

    private static FblElement ElementOf(AbmNode node, int[] starts, int length)
    {
        var type = AbmDefinition.TypeOfKind[node.Kind];
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = node.Label, ["notes"] = node.Notes };
        if (node.Kind == AbmNodeKinds.Retry) attributes["attempts"] = (long)node.RetryCount;
        if (node.Kind == AbmNodeKinds.Action) attributes["implicit"] = !node.HasKeyword;
        var span = new Span(Start(starts, node.Line, length), Start(starts, node.SubtreeEnd + 1, length));
        return new FblElement(node.Id, false, type, "item", false, attributes, node.ParentId, node.ParentId is null ? null : "children", null, null, span, node.Line + 1);
    }

    private static IReadOnlyList<Finding> Findings(AbmModel model)
    {
        if (model.SectionLine is null)
        {
            return [NoBehavior("This file has no Behavior heading, so there is no behavior tree to draw. Add a node to start one.", 0)];
        }

        return model.Nodes.Count == 0
            ? [NoBehavior("The Behavior section holds no list yet. Add a node to start the tree.", model.SectionLine.Value)]
            : [];
    }

    private static Finding NoBehavior(string message, int line) =>
        new(AbmRuleIds.NoBehavior, FindingSeverity.Info, message, new SourceLocation("", line + 1, 1, 0));

    private static byte[] Body(IReadOnlyList<PluginFile> files) =>
        files.FirstOrDefault(file => file.RelativePath.Length == 0)?.Bytes ?? throw new ArgumentException("A behavior model is a file body.", nameof(files));

    private static string Text(object? value) => value switch
    {
        null => "",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static int? Number(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        double number when double.IsInteger(number) && number is >= int.MinValue and <= int.MaxValue => (int)number,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        _ => null,
    };

    /// <summary>The byte offset each line starts at: 0, then after every line feed.</summary>
    private static int[] LineStarts(byte[] bytes)
    {
        List<int> starts = [0];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n') starts.Add(index + 1);
        }
        return [.. starts];
    }

    private static int Start(int[] starts, int line, int length) => line < starts.Length ? starts[line] : length;
}
