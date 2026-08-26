using Serilog;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Changes one property of one element, by rewriting the lines that declare it and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is the only thing in the module that writes, and the design calls it the riskiest
/// component here for a reason: the file is executable configuration the repository already owns,
/// so a stray line is a broken build and a diff somebody has to review. Every edit is therefore
/// scoped to a property's own lines - found inside the element's range, at the element's own
/// indentation - and applied through <see cref="PipelineDocument"/>'s splice, which cannot touch
/// anything outside the range it is given.
/// </para>
/// <para>
/// Three properties are editable and no more (Requirement 9.1): <c>displayName</c>, <c>dependsOn</c>
/// and <c>enabled</c>. That is a deliberately narrow set - the ones a reader of a diagram wants to
/// change while looking at it - and naming them as three methods rather than taking a key keeps it
/// from quietly widening.
/// </para>
/// <para>
/// Nothing here reflows. A stage gaining a <c>dependsOn</c> it never had gets new lines inserted
/// after its declaration, at the indentation its siblings use; the rest of the block is not
/// rewritten to accommodate them.
/// </para>
/// </remarks>
public sealed class PipelineWriter
{
    private static readonly ILogger _logger = Log.ForContext<PipelineWriter>();

    private readonly PipelineDocument _document;

    /// <summary>Creates a writer over <paramref name="document"/>.</summary>
    public PipelineWriter(PipelineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    /// <summary>
    /// Sets an element's <c>displayName</c>, or removes it when <paramref name="value"/> is empty -
    /// which is not the same as setting it to nothing, since an element without one falls back to
    /// its own identifying value.
    /// </summary>
    /// <returns>Whether the document changed.</returns>
    public bool SetDisplayName(PipelineEditTarget element, string value) => SetScalar(element, "displayName", value);

    /// <summary>Sets an element's <c>enabled</c>, or removes it when <paramref name="value"/> is empty.</summary>
    /// <returns>Whether the document changed.</returns>
    public bool SetEnabled(PipelineEditTarget element, string value) => SetScalar(element, "enabled", value);

    /// <summary>
    /// Sets an element's <c>dependsOn</c> to <paramref name="names"/>.
    /// </summary>
    /// <remarks>
    /// The three forms the schema allows are all written here, chosen to keep the diff small: one
    /// name goes in as a scalar, several as a block list, and none as <c>[]</c> - which is a real
    /// instruction meaning "wait for nothing", and so is written rather than removed.
    /// </remarks>
    /// <returns>Whether the document changed.</returns>
    public bool SetDependsOn(PipelineEditTarget element, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Guard(element);

        var indent = PropertyIndent(element);
        var lines = names.Count switch
        {
            0 => [$"{Spaces(indent)}dependsOn: []"],
            1 => [$"{Spaces(indent)}dependsOn: {Scalarise(names[0])}"],
            _ => new[] { $"{Spaces(indent)}dependsOn:" }
                .Concat(names.Select(name => $"{Spaces(indent + 2)}- {Scalarise(name)}"))
                .ToArray(),
        };

        return Write(element, "dependsOn", lines);
    }

    /// <summary>
    /// Removes an element's <c>dependsOn</c> entirely, putting it back on the schema's default -
    /// for a stage, the stage declared before it; for a job, nothing.
    /// </summary>
    /// <returns>Whether the document changed.</returns>
    public bool ClearDependsOn(PipelineEditTarget element)
    {
        Guard(element);
        return Write(element, "dependsOn", []);
    }

    /// <summary>
    /// Adds a whole element after <paramref name="after"/>, or as the first entry of the list
    /// <paramref name="listKeyLine"/> declares when there is nothing to come after.
    /// </summary>
    /// <remarks>
    /// A list's first entry and its fifth go in different places: the fifth follows its
    /// predecessor, and the first follows the key that opens the list. Both are inserts rather
    /// than reflows - nothing already in the file is rewritten to accommodate the new lines.
    /// </remarks>
    /// <param name="after">The element the new one follows, or null for the first in its list.</param>
    /// <param name="listKeyLine">The line the list's key sits on, used when <paramref name="after"/> is null.</param>
    /// <param name="lines">The element's own lines, already indented.</param>
    public void InsertElement(PipelineEditTarget? after, int listKeyLine, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return;
        }

        if (after is not null)
        {
            Guard(after);
            _document.Insert(after.Lines.End + 1, lines);
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(listKeyLine);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(listKeyLine, _document.Lines.Count);
        _document.Insert(listKeyLine + 1, lines);
    }

    /// <summary>
    /// Removes a whole element, and the blank lines that separated it from the next one.
    /// </summary>
    /// <remarks>
    /// The trailing blanks go with it deliberately. A pipeline that separates its stages with a
    /// blank line each would otherwise accumulate a run of them wherever a stage was removed, and
    /// that is a diff the user did not ask for even though every line of it is whitespace.
    /// </remarks>
    /// <returns>Whether the document changed.</returns>
    public bool RemoveElement(PipelineEditTarget element)
    {
        Guard(element);

        var end = element.Lines.End;
        while (end + 1 < _document.Lines.Count && _document.Lines[end + 1].IsBlank)
        {
            end++;
        }

        _document.Remove(new PipelineLineRange(element.Lines.Start, end));
        return true;
    }

    /// <summary>
    /// Moves one element to sit after another within the same list, or to the front of it.
    /// </summary>
    /// <remarks>
    /// Reordering is a lift and a drop, in that order, and the drop point has to be worked out
    /// before the lift: once the lines are gone every range below them has moved, and using a
    /// range measured beforehand would insert in the wrong place. That is the whole of the bug
    /// this method exists to not have.
    /// </remarks>
    /// <param name="element">What to move.</param>
    /// <param name="after">What it should follow, or null to move it to the front of the list.</param>
    /// <param name="listKeyLine">The line the list's key sits on, used when <paramref name="after"/> is null.</param>
    /// <returns>Whether the document changed.</returns>
    public bool MoveElement(PipelineEditTarget element, PipelineEditTarget? after, int listKeyLine)
    {
        Guard(element);
        if (after is not null)
        {
            Guard(after);
            if (after.Lines.Start == element.Lines.Start)
            {
                // Moving something to after itself is a no-op, and treating it as one keeps the
                // arithmetic below from having to mean anything in that case.
                return false;
            }
        }

        var moved = Enumerable.Range(element.Lines.Start, element.Lines.Length)
            .Select(index => _document.Lines[index].Text)
            .ToList();

        var target = after is null ? listKeyLine + 1 : after.Lines.End + 1;
        if (target > element.Lines.End)
        {
            // The destination is below what is about to be removed, so it moves up by that many.
            target -= element.Lines.Length;
        }

        _document.Remove(element.Lines);
        _document.Insert(target, moved);
        return true;
    }

    /// <summary>
    /// Writes a one-line property, keeping any comment that was trailing on the line it replaces -
    /// the comment is about that property, and losing it while renaming something would be an edit
    /// nobody asked for.
    /// </summary>
    private bool SetScalar(PipelineEditTarget element, string key, string value)
    {
        Guard(element);
        if (value.Length == 0)
        {
            return Write(element, key, []);
        }

        var indent = PropertyIndent(element);
        var trailing = Find(element, key, indent) is { } existing ? CommentOn(_document.Lines[existing.Start].Text) : "";
        return Write(element, key, [$"{Spaces(indent)}{key}: {Scalarise(value)}{trailing}"]);
    }

    /// <summary>
    /// Replaces the property's lines where it has some, inserts them where it does not, and removes
    /// them when <paramref name="lines"/> is empty.
    /// </summary>
    private bool Write(PipelineEditTarget element, string key, IReadOnlyList<string> lines)
    {
        var existing = Find(element, key, PropertyIndent(element));
        if (existing is { } range)
        {
            if (lines.Count == 0)
            {
                _document.Remove(range);
                return true;
            }

            if (Unchanged(range, lines))
            {
                return false;
            }

            _document.Replace(range, lines);
            return true;
        }

        if (lines.Count == 0)
        {
            return false;
        }

        // Nothing to replace, so the property is new: it goes on the line after the element's own
        // declaration, which is where a hand-written one would have gone and needs no reflowing.
        _logger.Debug("Adding {Key} to {Element} at {Range}", key, element.Id, element.Lines);
        _document.Insert(element.Lines.Start + 1, lines);
        return true;
    }

    /// <summary>
    /// Whether the lines already say exactly this, in which case writing them would put a
    /// no-op change in somebody's diff.
    /// </summary>
    private bool Unchanged(PipelineLineRange range, IReadOnlyList<string> lines) =>
        range.Length == lines.Count &&
        Enumerable.Range(0, lines.Count).All(offset =>
            string.Equals(_document.Lines[range.Start + offset].Text, lines[offset], StringComparison.Ordinal));

    /// <summary>
    /// The lines a property occupies within an element: its key line, and everything indented under
    /// it.
    /// </summary>
    /// <remarks>
    /// Matching on exact indentation is what keeps a stage's <c>displayName</c> from being found in
    /// one of its jobs - a job's properties are always further in - and what stops the search
    /// descending into blocks it has no business editing.
    /// </remarks>
    private PipelineLineRange? Find(PipelineEditTarget element, string key, int indent)
    {
        for (var index = element.Lines.Start; index <= element.Lines.End; index++)
        {
            var line = _document.Lines[index];
            if (line.Indent != indent || !StartsWithKey(line.Text, indent, key))
            {
                continue;
            }

            var end = index;
            while (end + 1 <= element.Lines.End &&
                !_document.Lines[end + 1].IsBlank &&
                _document.Lines[end + 1].Indent > indent)
            {
                end++;
            }

            return new PipelineLineRange(index, end);
        }

        return null;
    }

    /// <summary>
    /// Whether a line declares <paramref name="key"/> at <paramref name="indent"/> - the key
    /// itself, followed by its colon, and not merely a key that starts the same way.
    /// </summary>
    private static bool StartsWithKey(string text, int indent, string key)
    {
        var end = indent + key.Length;
        return text.Length > end &&
            text.AsSpan(indent, key.Length).SequenceEqual(key) &&
            text[end] == ':';
    }

    /// <summary>
    /// The column an element's own properties sit at.
    /// </summary>
    /// <remarks>
    /// A stage, job or step declared as a sequence entry starts <c>- stage: Build</c>, and its
    /// siblings line up with the key rather than the dash - two columns further in. An element that
    /// is not a sequence entry, such as the implicit stage wrapped around a bare <c>jobs</c> list,
    /// has its properties where it is. Taking this from the file rather than assuming two spaces is
    /// what lets an unindented pipeline be edited without being reformatted.
    /// </remarks>
    private int PropertyIndent(PipelineEditTarget element)
    {
        var first = _document.Lines[element.Lines.Start];
        var text = first.Text.AsSpan(first.Indent);
        return text.StartsWith("- ") || text.SequenceEqual("-") ? first.Indent + 2 : first.Indent;
    }

    /// <summary>
    /// The trailing comment on a line, with the spacing before it, or empty where there is none.
    /// </summary>
    /// <remarks>
    /// A <c>#</c> only starts a comment when it follows whitespace and is not inside quotes, so
    /// this scans rather than searching - <c>displayName: "Build #2"</c> has no comment on it, and
    /// treating one as present would truncate the name.
    /// </remarks>
    private static string CommentOn(string text)
    {
        var quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character == '#' && index > 0 && char.IsWhiteSpace(text[index - 1]))
            {
                return text[(LastNonSpaceBefore(text, index) + 1)..];
            }
        }

        return "";
    }

    private static int LastNonSpaceBefore(string text, int index)
    {
        var at = index - 1;
        while (at >= 0 && text[at] == ' ')
        {
            at--;
        }

        return at;
    }

    /// <summary>
    /// A value as a YAML scalar, quoted only where leaving it bare would change what it means.
    /// </summary>
    /// <remarks>
    /// Left bare wherever possible, because a quote appearing around a name that never had one is
    /// a diff line a reviewer has to think about. An expression is left exactly as written: quoting
    /// <c>${{ parameters.name }}</c> would still be valid YAML but would stop being an expression
    /// in some positions, and rewriting one while editing something else is precisely what this
    /// module promises not to do.
    /// </remarks>
    private static string Scalarise(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (value.StartsWith("${{", StringComparison.Ordinal) || value.StartsWith("$[", StringComparison.Ordinal))
        {
            return value;
        }

        var needsQuotes =
            value != value.Trim() ||
            value.Contains(": ", StringComparison.Ordinal) ||
            value.EndsWith(':') ||
            value.Contains(" #", StringComparison.Ordinal) ||
            value.Contains('\n') ||
            "-?:,[]{}#&*!|>'\"%@`".Contains(value[0]);

        return needsQuotes
            ? "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string Spaces(int count) => new(' ', count);

    private void Guard(PipelineEditTarget element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Lines.Start < 0 || element.Lines.End >= _document.Lines.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(element),
                $"{element.Lines} is outside a document of {_document.Lines.Count} lines.");
        }

        // An element whose text is not in this file has a range pointing at lines that belong to
        // something else, so writing through it would edit the wrong thing entirely. The UI stops
        // this long before here; this is the line that makes it impossible rather than unlikely.
        if (!element.IsEditable)
        {
            throw new InvalidOperationException($"{element.Id} cannot be edited here. {element.ReadOnlyReason}");
        }
    }
}
