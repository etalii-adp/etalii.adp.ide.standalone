using System.Globalization;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// This reading's edit gestures. Everything triple-shaped - a target, the type triple of a new
/// shape, <c>sh:deactivated</c>, a renamed term, a rewritten <c>sh:name</c> - dispatches through
/// the family <see cref="RdfWriter"/>'s named operations rather than splicing privately; only the
/// two operations whose definitions speak SHACL rather than Turtle live here
/// (shacl-diagram Requirement 5, and the family's writer-boundary rule).
/// </summary>
/// <remarks>
/// Every operation answers with an empty string on success or a refusal sentence from
/// <see cref="ShaclRefusals"/> before any splice, and a refused document is untouched to the byte.
/// Offsets in <see cref="RdfTriple"/> are the parser's working coordinates and are valid only for
/// the parse they came from, so a caller performing several operations reparses between them.
/// </remarks>
public static class ShaclWriter
{
    /// <summary>
    /// Splices one <c>sh:property [ … ] ;</c> continuation into an IRI-named shape's block.
    /// </summary>
    /// <remarks>
    /// Permitted under the **blank-node identity boundary** although it writes a blank node,
    /// because the command and its inverse are keyed to the IRI-named parent whose lines the
    /// splice touches: the blank node is created, never addressed. Mutating or removing an
    /// existing blank-rooted shape is a different act and is refused (Requirement 3.3).
    /// </remarks>
    public static string AppendPropertyShapeBlock(
        RdfDocument document,
        RdfModel model,
        string shapeIri,
        string pathIri,
        ShaclPropertyShapeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(shapeIri))
        {
            return ShaclRefusals.BlankRooted;
        }

        if (string.IsNullOrWhiteSpace(pathIri))
        {
            return ShaclRefusals.PathRequired;
        }

        var anchor = model.Triples
            .Where(triple => triple.Subject is IriTerm subject && subject.Iri == shapeIri)
            .OrderBy(triple => triple.TerminatorOffset)
            .LastOrDefault();

        if (anchor is null)
        {
            return ShaclRefusals.NoSuchShape;
        }

        var block = PropertyBlock(model, pathIri, options ?? new ShaclPropertyShapeOptions());

        // The statement's terminating '.' becomes a ';' and the new pair goes on its own line
        // below, indented as the block's continuation lines are - the family AddTriple shape,
        // reproduced here because the object is a blank block no term writer will emit.
        var starts = LineStarts(document);
        var terminatorLine = LineOf(starts, anchor.TerminatorOffset);
        var terminatorColumn = anchor.TerminatorOffset - starts[terminatorLine];
        var lineText = document.Lines[terminatorLine].Text;
        if (terminatorColumn < 0 || terminatorColumn >= lineText.Length || lineText[terminatorColumn] != '.')
        {
            // The offsets did not describe this text - a stale model, which is the one thing that
            // must never splice. Refusing beats writing at a guessed position.
            return ShaclRefusals.NoSuchShape;
        }

        var indent = anchor.Statement.Length > 1
            ? IndentOf(document.Lines[anchor.Statement.Start + 1].Text)
            : IndentOf(document.Lines[anchor.Statement.Start].Text) + "    ";

        document.Replace(
            new LineRange(terminatorLine, terminatorLine),
            [lineText[..terminatorColumn] + ";" + lineText[(terminatorColumn + 1)..]]);
        document.Insert(terminatorLine + 1, [$"{indent}{block} ."]);
        return "";
    }

    /// <summary>Declares one more target on an IRI-named shape, through the family writer.</summary>
    public static string AddTarget(RdfDocument document, RdfModel model, string shapeIri, string targetPredicateIri, RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(term);

        if (string.IsNullOrWhiteSpace(shapeIri))
        {
            return ShaclRefusals.BlankRooted;
        }

        if (!ShaclVocabulary.TargetPredicates.Contains(targetPredicateIri))
        {
            return ShaclRefusals.NoSuchTarget;
        }

        return RdfWriter.AddTriple(document, model, shapeIri, targetPredicateIri, term);
    }

    /// <summary>
    /// Removes one target declaration, addressed the way a chip carries it: the owning shape's
    /// IRI, the target predicate, and the targeted term - no canvas selection involved
    /// (Requirement 5.3, over the chip address of Requirement 1.3).
    /// </summary>
    public static string RemoveTarget(RdfDocument document, RdfModel model, string shapeIri, string targetPredicateIri, string termIri)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(shapeIri))
        {
            return ShaclRefusals.BlankRooted;
        }

        if (string.IsNullOrWhiteSpace(targetPredicateIri))
        {
            // The implicit class target states itself through the file's own class typing.
            return ShaclRefusals.ImplicitTarget;
        }

        var triple = model.Triples.FirstOrDefault(candidate =>
            candidate.Subject is IriTerm subject && subject.Iri == shapeIri
            && candidate.Predicate.Iri == targetPredicateIri
            && candidate.Object is IriTerm target && target.Iri == termIri);

        return triple is null ? ShaclRefusals.NoSuchTarget : RdfWriter.RemoveTriple(document, model, triple);
    }

    /// <summary>States a new node shape: one subject with one <c>rdf:type sh:NodeShape</c> triple.</summary>
    public static string CreateNodeShape(RdfDocument document, RdfModel model, string shapeIri)
    {
        ArgumentNullException.ThrowIfNull(model);

        return string.IsNullOrWhiteSpace(shapeIri)
            ? ShaclRefusals.NoSuchShape
            : RdfWriter.AddTriple(document, model, shapeIri, RdfVocabulary.Type, new IriTerm(ShaclVocabulary.NodeShape, "sh:NodeShape"));
    }

    /// <summary>
    /// Switches a shape off or back on: adds or removes its <c>sh:deactivated true</c> triple.
    /// </summary>
    public static string SetDeactivated(RdfDocument document, RdfModel model, string shapeIri, bool deactivated)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(shapeIri))
        {
            return ShaclRefusals.BlankRooted;
        }

        var existing = model.Triples.FirstOrDefault(triple =>
            triple.Subject is IriTerm subject && subject.Iri == shapeIri
            && triple.Predicate.Iri == ShaclVocabulary.Deactivated);

        if (deactivated)
        {
            return existing is not null
                ? "" // already off; nothing to splice, and saying so beats writing a duplicate
                : RdfWriter.AddTriple(document, model, shapeIri, ShaclVocabulary.Deactivated, TrueLiteral());
        }

        return existing is null ? "" : RdfWriter.RemoveTriple(document, model, existing);
    }

    /// <summary>
    /// Sets a shape's <c>sh:name</c> or <c>sh:description</c>: the family
    /// <see cref="RdfWriter.ReplaceObjectLiteral"/> where one is already stated - a single-literal
    /// rewrite is exactly what that operation is for - and an added triple where none is.
    /// </summary>
    public static string SetLiteral(RdfDocument document, RdfModel model, string shapeIri, string predicateIri, string value)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(shapeIri))
        {
            return ShaclRefusals.BlankRooted;
        }

        var existing = model.Triples.FirstOrDefault(triple =>
            triple.Subject is IriTerm subject && subject.Iri == shapeIri
            && triple.Predicate.Iri == predicateIri
            && triple.Object is LiteralTerm);

        return existing is not null
            ? RdfWriter.ReplaceObjectLiteral(document, model, existing, value, null, null)
            : RdfWriter.AddTriple(document, model, shapeIri, predicateIri, new LiteralTerm(value, null, null, $"\"{value}\""));
    }

    /// <summary>The <c>sh:property [ … ]</c> text, prefixes reused and never invented.</summary>
    private static string PropertyBlock(RdfModel model, string pathIri, ShaclPropertyShapeOptions options)
    {
        var parts = new List<string> { $"{Term(model, ShaclVocabulary.Path)} {Term(model, pathIri)}" };

        if (options.DatatypeIri is { Length: > 0 } datatype)
        {
            parts.Add($"{Term(model, ShaclVocabulary.Datatype)} {Term(model, datatype)}");
        }

        if (options.MinCount is { } min)
        {
            parts.Add($"{Term(model, ShaclVocabulary.MinCount)} {min.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.MaxCount is { } max)
        {
            parts.Add($"{Term(model, ShaclVocabulary.MaxCount)} {max.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.Name is { Length: > 0 } name)
        {
            parts.Add($"{Term(model, ShaclVocabulary.Name)} \"{Escape(name)}\"");
        }

        return $"{Term(model, ShaclVocabulary.Property)} [ {string.Join(" ; ", parts)} ]";
    }

    private static string Term(RdfModel model, string iri) => RdfWriter.Compress(model, iri);

    private static LiteralTerm TrueLiteral() =>
        new("true", "http://www.w3.org/2001/XMLSchema#boolean", null, "true");

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// The start offset of every line, for turning the parser's absolute offsets into a line and
    /// column. Mirrors the family writer's own helper rather than reaching into it.
    /// </summary>
    private static List<int> LineStarts(RdfDocument document)
    {
        var starts = new List<int>(document.Lines.Count) { 0 };
        var offset = 0;
        foreach (var line in document.Lines)
        {
            offset += line.Text.Length + line.Ending.Length;
            starts.Add(offset);
        }

        return starts;
    }

    private static int LineOf(List<int> starts, int offset)
    {
        for (var index = 0; index < starts.Count - 1; index++)
        {
            if (offset < starts[index + 1])
            {
                return index;
            }
        }

        return starts.Count - 2;
    }

    private static string IndentOf(string line)
    {
        var length = 0;
        while (length < line.Length && (line[length] == ' ' || line[length] == '\t'))
        {
            length++;
        }

        return line[..length];
    }
}
