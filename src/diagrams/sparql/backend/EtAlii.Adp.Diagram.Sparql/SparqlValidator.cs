using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Judges a query file, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 7): facts about the text, and nothing else. It never executes
/// the query, never contacts an endpoint, and never resolves a <c>SERVICE</c> clause - not as a
/// policy applied here, but because this module references no HTTP client at all.
/// </summary>
public sealed class SparqlValidator(DiagramOrigin origin) : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<SparqlValidator>();

    /// <summary>The rule reported when the file is not a SPARQL query this can read - SPARQL Update included.</summary>
    public const string UnparseableRuleId = "sparql.unparseable";

    /// <summary>The rule reported when a projected variable never appears in the where clause.</summary>
    public const string UnboundProjectionRuleId = "sparql.unbound-projection";

    /// <summary>The rule reported when a prefix is declared but never used.</summary>
    public const string UnusedPrefixRuleId = "sparql.unused-prefix";

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
        var text = request.Document ?? "";
        SparqlQueryModel model;
        try
        {
            model = SparqlParser.Parse(text);
        }
        catch (SparqlParseException exception)
        {
            // One finding, not a list of consequences: running the rules over an empty model
            // would bury the reason the file could not be read (Requirement 7.1).
            _logger.Debug(exception, "{BaseName} does not parse at line {Line}", request.BaseName, exception.Line);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not a SPARQL query that can be read: {exception.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)Math.Max(exception.Line, 1))),
            ]);
        }

        return ValueTask.FromResult(Judge(model, text));
    }

    /// <summary>The Requirement 7 findings over a parsed query - a pure function, tested as one.</summary>
    internal static IReadOnlyList<DiagramProblem> Judge(SparqlQueryModel model, string text)
    {
        var problems = new List<DiagramProblem>();

        // A projected variable the pattern never binds is legal SPARQL and yields a silently
        // empty column - exactly the kind of lie a reader wants caught (Requirement 7.2).
        foreach (var item in model.Projection)
        {
            if (!model.Variables.TryGetValue(item.Name, out var usage))
            {
                continue;
            }

            if (usage.PatternOccurrences == 0 && usage.DefiningExpression.Length == 0)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"?{item.Name} is selected but never appears in the where clause, so its column will always be unbound.",
                    UnboundProjectionRuleId,
                    new DiagramProblemLineLocation(1)));
            }
        }

        // An unused prefix declaration is harmless but misleading - info, at its own line
        // (Requirement 7.3). A prefix used but not declared is a parse error, reported above.
        foreach (var prefix in model.Prefixes)
        {
            if (!IsUsed(prefix.Prefix, text))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Info,
                    $"The prefix '{prefix.Prefix}:' is declared but never used.",
                    UnusedPrefixRuleId,
                    new DiagramProblemLineLocation((uint)Math.Max(prefix.Line, 1))));
            }
        }

        return problems;
    }

    /// <summary>
    /// Whether the query uses <paramref name="prefix"/> anywhere outside its own declarations.
    /// Asked of the source text rather than the model, because a prefixed name can appear in an
    /// expression or a solution modifier - places the model keeps as written rather than as terms.
    /// </summary>
    private static bool IsUsed(string prefix, string text)
    {
        var needle = prefix + ":";
        var from = 0;

        while (true)
        {
            var at = text.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            from = at + needle.Length;

            // The declaration itself is not a use: skip an occurrence whose line starts with
            // PREFIX before it.
            var lineStart = text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
            var beforeOnLine = text[lineStart..at].TrimStart();
            if (beforeOnLine.StartsWith("PREFIX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }
    }
}
