using System.Text.RegularExpressions;
using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Judges one of the family's documents, through the seam every diagram type's rules reach the
/// Errors and Warnings panel by (rdf-diagram Requirement 7): facts about the text, no inference,
/// no network. Reading-specific rules stay with the sibling readings.
/// </summary>
/// <remarks>
/// A file that will not parse is reported as one problem naming the line, since running the rules
/// over an empty model would bury that under a list of consequences (Requirement 7.1).
/// </remarks>
public sealed partial class RdfValidator(DiagramOrigin origin) : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<RdfValidator>();

    /// <summary>The rule reported when the file is not Turtle or N-Triples this can read.</summary>
    public const string UnparseableRuleId = "rdf.unparseable";

    /// <summary>The rule reported when a prefix is declared more than once.</summary>
    public const string DuplicatePrefixRuleId = "rdf.duplicate-prefix";

    /// <summary>The rule reported - once - when relative IRIs appear in a document with no base.</summary>
    public const string RelativeIriRuleId = "rdf.relative-iri";

    /// <summary>The rule reported for a language tag no speaker could have meant.</summary>
    public const string LanguageTagRuleId = "rdf.language-tag";

    /// <summary>The rule reported for a datatype in the XSD namespace that XSD does not define.</summary>
    public const string UnknownDatatypeRuleId = "rdf.unknown-datatype";

    private static readonly HashSet<string> _xsdTypes =
    [
        "string", "boolean", "decimal", "integer", "double", "float", "date", "time", "dateTime",
        "dateTimeStamp", "duration", "gYear", "gMonth", "gDay", "gYearMonth", "gMonthDay",
        "hexBinary", "base64Binary", "anyURI", "language", "normalizedString", "token", "long",
        "int", "short", "byte", "nonNegativeInteger", "positiveInteger", "negativeInteger",
        "nonPositiveInteger", "unsignedLong", "unsignedInt", "unsignedShort", "unsignedByte",
    ];

    private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        // Reason: Can still be null if the document is empty.
        var document = RdfDocument.Parse(request.Document ?? "");
        RdfModel model;
        try
        {
            model = RdfParser.Parse(document);
        }
        catch (RdfParseException exception)
        {
            _logger.Debug(exception, "{BaseName} does not parse at line {Line}", request.BaseName, exception.Line);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not RDF that can be read: {exception.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)Math.Max(exception.Line, 1))),
            ]);
        }

        return ValueTask.FromResult(Judge(model));
    }

    /// <summary>The Requirement 7 findings over a parsed model - a pure function, tested as one.</summary>
    internal static IReadOnlyList<DiagramProblem> Judge(RdfModel model)
    {
        var problems = new List<DiagramProblem>();

        // A prefix declared twice: the later declaration silently wins, which is exactly the
        // kind of fact worth a warning at the line that changes the meaning (Requirement 7.2).
        foreach (var group in model.Prefixes.GroupBy(p => p.Prefix).Where(g => g.Count() > 1))
        {
            foreach (var redeclaration in group.Skip(1))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The prefix '{redeclaration.Prefix}:' is declared more than once; from here on it means {redeclaration.Iri}, which changes what every earlier-styled use would say.",
                    DuplicatePrefixRuleId,
                    new DiagramProblemLineLocation((uint)(redeclaration.Line + 1))));
            }
        }

        // Relative IRIs without a base resolve differently in every tool that guesses - warned
        // once at the first occurrence rather than once per term (Requirement 7.3).
        if (model.BaseIri is null)
        {
            var relative = model.Triples.FirstOrDefault(t => Terms(t).OfType<IriTerm>().Any(term => !RdfParser.IsAbsolute(term.Iri)));
            if (relative is not null)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    "This document uses relative IRIs but declares no base, so their meaning depends on where the file happens to sit. Declare an @base.",
                    RelativeIriRuleId,
                    new DiagramProblemLineLocation((uint)(relative.Span.StartLine + 1))));
            }
        }

        foreach (var triple in model.Triples)
        {
            if (triple.Object is not LiteralTerm literal)
            {
                continue;
            }

            if (literal.Language is { Length: > 0 } tag && !LanguageTag().IsMatch(tag))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'@{tag}' is not a well-formed language tag (letters, then optional '-' groups of letters and digits).",
                    LanguageTagRuleId,
                    new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1))));
            }

            if (literal.DatatypeIri is { } datatype
                && datatype.StartsWith(XsdNamespace, StringComparison.Ordinal)
                && !_xsdTypes.Contains(datatype[XsdNamespace.Length..]))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"xsd:{datatype[XsdNamespace.Length..]} is not a datatype XSD defines; tools that check will refuse the value.",
                    UnknownDatatypeRuleId,
                    new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1))));
            }
        }

        return problems;

        static IEnumerable<RdfTerm> Terms(RdfTriple triple) => [triple.Subject, triple.Predicate, triple.Object];
    }

    [GeneratedRegex("^[A-Za-z]+(-[A-Za-z0-9]+)*$")]
    private static partial Regex LanguageTag();
}
