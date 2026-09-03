using System.Text;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The family's named splice operations - THE writer every reading's edit gesture dispatches
/// through, under reading-specific names. Each operation answers with an empty string on success
/// or a refusal sentence before any splice; a refused document is untouched to the byte.
/// </summary>
/// <remarks>
/// <para>
/// Every operation takes the model beside the document because the model carries the offsets the
/// parser recorded; offsets are valid only for the parse they came from, so a caller performing
/// several operations reparses between them (as <see cref="RemoveResource"/> does itself).
/// </para>
/// <para>
/// Terms are written with the document's own declared prefixes where one matches, and as full
/// bracketed IRIs otherwise - a writer never invents a declaration as a side effect
/// (Requirement 5.7). N-Triples flows through the same operations degenerately: every triple owns
/// exactly one line, so every removal is a whole-line removal and every splice a line rewrite.
/// </para>
/// </remarks>
public static class RdfWriter
{
    private const string BlankRefusal =
        "That element is rooted in a blank node, which has no identity that survives a reparse, so this edit cannot land in the file. Name the node with an IRI to edit it.";

    /// <summary>
    /// States one more triple: appended to the subject's existing statement as a <c>;</c>
    /// continuation matching its indentation, or as a new statement at the end of the document.
    /// </summary>
    public static string AddTriple(RdfDocument document, RdfModel model, string subjectIri, string predicateIri, RdfTerm objectTerm)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateIri);
        ArgumentNullException.ThrowIfNull(objectTerm);

        if (objectTerm is BlankTerm)
        {
            return BlankRefusal;
        }

        // rdf:type is written as Turtle's own keyword, the way an author would.
        var predicate = predicateIri == RdfVocabulary.Type ? "a" : Compress(model, predicateIri);
        var written = $"{predicate} {WriteTerm(model, objectTerm)}";
        var anchor = model.Triples
            .Where(t => t.Subject is IriTerm subject && subject.Iri == subjectIri)
            .OrderBy(t => t.TerminatorOffset)
            .LastOrDefault();

        if (anchor is null)
        {
            var statement = $"{Compress(model, subjectIri)} {written} .";
            var index = document.Lines.Count;
            if (index > 0 && !document.Lines[^1].IsBlank)
            {
                document.Insert(index, ["", statement]);
            }
            else
            {
                document.Insert(index, [statement]);
            }

            return "";
        }

        // The statement's '.' becomes a ';', and the new pair goes on its own line below,
        // indented the way the block's continuation lines are - or one step in from the
        // subject where the statement was a single line.
        var text = document.Text;
        var starts = LineStarts(text);
        var terminatorLine = LineOf(starts, anchor.TerminatorOffset);
        var terminatorColumn = anchor.TerminatorOffset - starts[terminatorLine];
        var lineText = document.Lines[terminatorLine].Text;

        var indent = anchor.Statement.Length > 1
            ? IndentOf(document.Lines[anchor.Statement.Start + 1].Text)
            : IndentOf(document.Lines[anchor.Statement.Start].Text) + "    ";

        document.Replace(
            new LineRange(terminatorLine, terminatorLine),
            [lineText[..terminatorColumn] + ";" + lineText[(terminatorColumn + 1)..]]);
        document.Insert(terminatorLine + 1, [$"{indent}{written} ."]);
        return "";
    }

    /// <summary>
    /// Removes one triple: its whole lines where it owns them, the exact fragment plus its
    /// stranded separator where it shares a line, and the whole statement - block and terminating
    /// <c>.</c> - where it is the statement's last triple.
    /// </summary>
    public static string RemoveTriple(RdfDocument document, RdfModel model, RdfTriple triple)
    {
        ArgumentNullException.ThrowIfNull(triple);

        if (triple.Subject is BlankTerm || triple.Object is BlankTerm)
        {
            return BlankRefusal;
        }

        return RemoveTripleAnchored(document, model, triple);
    }

    /// <summary>
    /// Removes one triple without the blank-node refusal, for a caller that has already anchored
    /// the edit to an IRI-named subject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blank-node identity boundary refuses edits that would have to be <em>keyed</em> to a
    /// blank node, because no such key survives a reparse. It does not forbid a blank node being
    /// swept by an edit keyed to something stable: removing an IRI-named SHACL shape takes the
    /// constraint blocks written inside it, and every one of those is a triple that has to be
    /// spliced. The caller owes the anchor and a whole-document inverse; this owes the splice.
    /// </para>
    /// <para>
    /// Internal, and deliberately not the public entry point - <see cref="RemoveTriple"/> keeps
    /// the refusal so nothing reaches this by default. The splice mechanics below are pure Turtle
    /// and so belong here rather than duplicated inside a reading, per the writer-boundary rule.
    /// </para>
    /// <para>
    /// <b>Reparse between every call.</b> A triple's offsets are valid only for the parse they
    /// came from, and this splice shifts everything after it, so a caller that collects several
    /// triples from one model and then loops over that captured list will write wrong bytes on
    /// the second call - silently, with no exception and no refusal. Re-run
    /// <see cref="RdfParser.Parse(RdfDocument)"/> after each removal and find the next victim in
    /// the fresh model, as <see cref="RemoveResource"/> does.
    /// </para>
    /// <para>
    /// <b>For wholesale sweeps, not surgical removal.</b> A triple written inside an inline blank
    /// property list carries the ENCLOSING statement's range, because the parser stamps one range
    /// from subject to terminating dot onto every triple the statement pends. When such a triple
    /// is the last of that statement, removing it takes the whole statement. That is correct for
    /// a caller removing the enclosing subject anyway; for a caller trying to remove one
    /// blank-rooted triple on its own it would take an unrelated statement with it.
    /// </para>
    /// </remarks>
    internal static string RemoveTripleAnchored(RdfDocument document, RdfModel model, RdfTriple triple)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(triple);

        if (!model.Triples.Contains(triple))
        {
            return "That triple is not part of the document as it stands - the file may have changed since it was read.";
        }

        var statementMates = model.Triples.Where(t => t.Statement == triple.Statement && !t.Equals(triple)).ToList();
        if (statementMates.Count == 0)
        {
            document.Remove(triple.Statement);
            return "";
        }

        var continuations = statementMates.Where(m =>
            m.IsListContinuation
            && m.Subject.Equals(triple.Subject)
            && m.Predicate.Iri == triple.Predicate.Iri).ToList();

        if (triple.IsListContinuation || continuations.Count > 0)
        {
            // An object-list member: the object goes, the predicate stays for the rest.
            var (start, end) = ExtendOverSeparator(document.Text, triple.ObjectStart, triple.ObjectEnd, ',');
            RemoveRegion(document, start, end);
        }
        else
        {
            // A sole pair among others: predicate and object go, with one ';'.
            var (start, end) = ExtendOverSeparator(document.Text, triple.SpanStart, triple.SpanEnd, ';');
            RemoveRegion(document, start, end);
        }

        return "";
    }

    /// <summary>
    /// Removes every triple the IRI-named resource is subject or object of, bottom-up so earlier
    /// removals never shift what later ones splice.
    /// </summary>
    public static string RemoveResource(RdfDocument document, RdfModel model, string iri)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);

        var matches = Matches(model, iri);
        if (matches.Count == 0)
        {
            return $"Nothing in the document names {iri}, so there is nothing to remove.";
        }

        if (matches.Any(t => t.Subject is BlankTerm || t.Object is BlankTerm))
        {
            return BlankRefusal;
        }

        // Each removal invalidates every recorded offset, so the model is reparsed per step and
        // the bottom-most match removed first.
        var current = model;
        while (true)
        {
            var next = Matches(current, iri).OrderByDescending(t => t.SpanStart).FirstOrDefault();
            if (next is null)
            {
                return "";
            }

            var refusal = RemoveTriple(document, current, next);
            if (refusal.Length > 0)
            {
                return refusal;
            }

            current = RdfParser.Parse(document);
        }

        static List<RdfTriple> Matches(RdfModel model, string iri) =>
            model.Triples
                .Where(t => (t.Subject is IriTerm s && s.Iri == iri) || (t.Object is IriTerm o && o.Iri == iri))
                .ToList();
    }

    /// <summary>
    /// Renames a term everywhere it occurs - subject, predicate, object, datatype, prefixed or
    /// full - in one operation, so no reference is ever stranded. Collisions are refused first.
    /// </summary>
    public static string RenameTerm(RdfDocument document, RdfModel model, string oldIri, string newIri)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(newIri);

        if (oldIri == newIri)
        {
            return "The new name is the same as the old one, so there is nothing to rename.";
        }

        if (model.Triples.Any(t => Names(t, newIri)))
        {
            return $"{newIri} already names something in this document, so renaming onto it would silently merge two resources. Pick an unused IRI.";
        }

        if (!model.Triples.Any(t => Names(t, oldIri)))
        {
            return $"Nothing in the document names {oldIri}, so there is nothing to rename.";
        }

        // Occurrences come from the tokens themselves rather than from triple spans, because a
        // subject's token is statement-level and a datatype's sits inside a literal - the tokens
        // see every spelling. Replacements land right to left so earlier offsets stay valid.
        var written = Compress(model, newIri);
        foreach (var (start, end) in TermOccurrences(document.Text, oldIri).OrderByDescending(o => o.Start))
        {
            ReplaceRegion(document, start, end, written);
        }

        return "";

        static bool Names(RdfTriple triple, string iri) =>
            (triple.Subject is IriTerm s && s.Iri == iri)
            || triple.Predicate.Iri == iri
            || (triple.Object is IriTerm o && o.Iri == iri)
            || (triple.Object is LiteralTerm l && l.DatatypeIri == iri);
    }

    /// <summary>
    /// Rewrites exactly one literal object token in place - lexical form, datatype annotation or
    /// language tag - leaving every neighbouring byte untouched. What a label or documentation
    /// edit is; family-level per the design's writer boundary rule.
    /// </summary>
    /// <remarks>
    /// Pass a null <paramref name="language"/> and <paramref name="datatypeIri"/> for a plain
    /// string; they are mutually exclusive, language winning where both arrive.
    /// </remarks>
    public static string ReplaceObjectLiteral(RdfDocument document, RdfModel model, RdfTriple triple, string lexical, string? language, string? datatypeIri)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(triple);
        ArgumentNullException.ThrowIfNull(lexical);

        if (!model.Triples.Contains(triple))
        {
            return "That triple is not part of the document as it stands - the file may have changed since it was read.";
        }

        if (triple.Object is not LiteralTerm)
        {
            return "That value is not a literal, so it cannot be rewritten as one. Rename or reconnect the resource instead.";
        }

        if (triple.Subject is BlankTerm)
        {
            return BlankRefusal;
        }

        var written = new StringBuilder("\"").Append(EscapeLiteral(lexical)).Append('"');
        if (language is { Length: > 0 })
        {
            written.Append('@').Append(language);
        }
        else if (datatypeIri is { Length: > 0 } && datatypeIri != RdfVocabulary.XsdString)
        {
            written.Append("^^").Append(Compress(model, datatypeIri));
        }

        ReplaceRegion(document, triple.ObjectStart, triple.ObjectEnd, written.ToString());
        return "";
    }

    /// <summary>Declares one more prefix, beside the existing declaration run.</summary>
    public static string AddPrefix(RdfDocument document, RdfModel model, string prefix, string iri)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);

        var existing = model.Expansion(prefix);
        if (existing is not null)
        {
            return existing == iri
                ? $"The prefix '{prefix}:' already expands to {iri}, so there is nothing to add."
                : $"The prefix '{prefix}:' is already declared as {existing}. Redeclaring it would change what every use of it means.";
        }

        var line = model.Prefixes.Count > 0
            ? model.Prefixes.Max(p => p.Line) + 1
            : model.BaseLine >= 0 ? model.BaseLine + 1 : 0;
        document.Insert(line, [$"@prefix {prefix}: <{iri}> ."]);
        return "";
    }

    /// <summary>
    /// The IRI written with the model's own declared prefixes where the longest-matching one
    /// yields a clean local name, and bracketed otherwise - never inventing a declaration.
    /// </summary>
    internal static string Compress(RdfModel model, string iri)
    {
        PrefixDeclaration? best = null;
        foreach (var declaration in model.Prefixes)
        {
            if (!iri.StartsWith(declaration.Iri, StringComparison.Ordinal) || iri.Length == declaration.Iri.Length)
            {
                continue;
            }

            if (best is null || declaration.Iri.Length >= best.Iri.Length)
            {
                best = declaration;
            }
        }

        if (best is null)
        {
            return $"<{iri}>";
        }

        var local = iri[best.Iri.Length..];
        return IsCleanLocalName(local) ? $"{best.Prefix}:{local}" : $"<{iri}>";
    }

    private static bool IsCleanLocalName(string local) =>
        local.Length > 0
        && local[0] != '.' && local[^1] != '.'
        && local.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');

    private static string WriteTerm(RdfModel model, RdfTerm term) =>
        term switch
        {
            IriTerm iri => Compress(model, iri.Iri),
            LiteralTerm { Language: { Length: > 0 } language } literal => $"\"{EscapeLiteral(literal.Lexical)}\"@{language}",
            LiteralTerm { DatatypeIri: { Length: > 0 } datatype } literal when datatype != RdfVocabulary.XsdString =>
                $"\"{EscapeLiteral(literal.Lexical)}\"^^{Compress(model, datatype)}",
            LiteralTerm literal => $"\"{EscapeLiteral(literal.Lexical)}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(term), "Blank nodes are refused before writing begins.")
        };

    private static string EscapeLiteral(string lexical)
    {
        var builder = new StringBuilder(lexical.Length);
        foreach (var character in lexical)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Grows a removal region over exactly one adjacent list separator and the whitespace binding
    /// it: the preceding one where there is one - so a last member takes the separator that
    /// announced it - and the following one otherwise, so a first member leaves its successor
    /// clean.
    /// </summary>
    private static (int Start, int End) ExtendOverSeparator(string text, int start, int end, char separator)
    {
        var before = start - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before]))
        {
            before--;
        }

        if (before >= 0 && text[before] == separator)
        {
            return (before, end);
        }

        var after = end;
        while (after < text.Length && char.IsWhiteSpace(text[after]) && text[after] is not ('\n' or '\r'))
        {
            after++;
        }

        if (after < text.Length && text[after] == separator)
        {
            after++;
            while (after < text.Length && char.IsWhiteSpace(text[after]) && text[after] is not ('\n' or '\r'))
            {
                after++;
            }

            return (start, after);
        }

        return (start, end);
    }

    /// <summary>
    /// Removes the characters in <c>[start, end)</c>, joining what remains of the first and last
    /// affected lines - and removing lines that are left holding only whitespace.
    /// </summary>
    private static void RemoveRegion(RdfDocument document, int start, int end)
    {
        var starts = LineStarts(document.Text);
        var firstLine = LineOf(starts, start);
        var lastLine = LineOf(starts, Math.Max(start, end - 1));

        var before = document.Lines[firstLine].Text[..(start - starts[firstLine])];
        var after = document.Lines[lastLine].Text[(end - starts[lastLine])..];

        if (before.TrimEnd().Length > 0 && after.Length > 0 && char.IsWhiteSpace(before[^1]) && char.IsWhiteSpace(after[0]))
        {
            before = before.TrimEnd();
        }

        var joined = before + after;
        if (joined.Trim().Length == 0)
        {
            document.Remove(new LineRange(firstLine, lastLine));
        }
        else
        {
            document.Replace(new LineRange(firstLine, lastLine), [joined]);
        }
    }

    /// <summary>Replaces the characters in <c>[start, end)</c> with <paramref name="replacement"/>, one line out.</summary>
    private static void ReplaceRegion(RdfDocument document, int start, int end, string replacement)
    {
        var starts = LineStarts(document.Text);
        var firstLine = LineOf(starts, start);
        var lastLine = LineOf(starts, Math.Max(start, end - 1));

        var before = document.Lines[firstLine].Text[..(start - starts[firstLine])];
        var after = document.Lines[lastLine].Text[(end - starts[lastLine])..];
        document.Replace(new LineRange(firstLine, lastLine), [before + replacement + after]);
    }

    /// <summary>
    /// Every token range in <paramref name="text"/> that names <paramref name="iri"/>, in any
    /// spelling: bracketed, prefixed under the declarations in force at that point, or as a
    /// literal's datatype. The keyword <c>a</c> is left alone - it is grammar, not a name.
    /// </summary>
    private static List<(int Start, int End)> TermOccurrences(string text, string iri)
    {
        var occurrences = new List<(int, int)>();
        var tokenizer = new RdfTokenizer(text);
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? baseIri = null;

        while (true)
        {
            var token = tokenizer.Next();
            switch (token.Kind)
            {
                case RdfTokenKind.EndOfFile:
                    return occurrences;

                case RdfTokenKind.PrefixDirective or RdfTokenKind.SparqlPrefix:
                {
                    var name = tokenizer.Next();
                    var value = tokenizer.Next();
                    if (name.Kind == RdfTokenKind.PrefixedName && value.Kind == RdfTokenKind.Iri)
                    {
                        prefixes[name.Value[..^1]] = Resolve(baseIri, value.Value[1..^1]);
                    }

                    break;
                }

                case RdfTokenKind.BaseDirective or RdfTokenKind.SparqlBase:
                {
                    var value = tokenizer.Next();
                    if (value.Kind == RdfTokenKind.Iri)
                    {
                        baseIri = Resolve(baseIri, value.Value[1..^1]);
                    }

                    break;
                }

                case RdfTokenKind.Iri when Resolve(baseIri, token.Value[1..^1]) == iri:
                    occurrences.Add((token.Start, token.End));
                    break;

                case RdfTokenKind.PrefixedName:
                {
                    var colon = token.Value.IndexOf(':');
                    if (prefixes.TryGetValue(token.Value[..colon], out var expansion)
                        && expansion + token.Value[(colon + 1)..] == iri)
                    {
                        occurrences.Add((token.Start, token.End));
                    }

                    break;
                }
            }
        }

        static string Resolve(string? baseIri, string candidate)
        {
            if (baseIri is null || candidate.Contains("://", StringComparison.Ordinal) || candidate.StartsWith("urn:", StringComparison.Ordinal) || candidate.StartsWith("mailto:", StringComparison.Ordinal))
            {
                return candidate;
            }

            return Uri.TryCreate(new Uri(baseIri, UriKind.Absolute), candidate, out var resolved)
                ? resolved.AbsoluteUri
                : candidate;
        }
    }

    private static string IndentOf(string line)
    {
        var length = 0;
        while (length < line.Length && char.IsWhiteSpace(line[length]))
        {
            length++;
        }

        return line[..length];
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    private static int LineOf(List<int> starts, int offset)
    {
        var low = 0;
        var high = starts.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (starts[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
