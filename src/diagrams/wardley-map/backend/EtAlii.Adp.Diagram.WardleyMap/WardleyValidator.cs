using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Carries the findings the definition derives (<see cref="WardleyDefinition.Problems"/>) to the Errors &amp; Warnings
/// panel, with the one rule that cannot be stated in it, <see cref="WardleySubmapRule"/> (Requirement 14.1).
/// </summary>
/// <remarks>
/// <para>
/// This class parses, asks the definition, and adds the one thing a model cannot answer: whether a `url` that looks like
/// a file path is a file that is actually in the project (Requirement 14.6). That check needs the project root, which
/// arrives on the request (wardley-map.md, gap 9).
/// </para>
/// <para>
/// Core neither interprets, ranks nor rewrites what comes back (Requirement 14.1); it routes it by origin and shows it.
/// </para>
/// </remarks>
/// <param name="documents">
/// The store, for the ids of the map's elements: a problem about an element is located at its id (finding A7), and the
/// ids are those the store holds beside the document it was asked about.
/// </param>
public sealed class WardleyValidator(IWardleyDocumentStore documents) : IDiagramValidator
{
    public const string SubmapMissingRuleId = WardleySubmapRule.RuleId;

    /// <summary>The rule reported for a line the parser passed over, at that line (code-structure B-25).</summary>
    public const string LinePassedOverRuleId = "wardley.line-passed-over";

    public ToolOrigin Origin => Diagram.WardleyMap.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var body = LineDocument.Parse(request.Document);
        var outcome = WardleyParser.Outcome(body);
        var map = outcome.Model;
        return ValueTask.FromResult(WardleyDefinition.Problems(
            body,
            map,
            outcome.Problems,
            IdentitiesOf(request),
            [.. WardleySubmapRule.UnresolvedReferences(map, request)]));
    }

    /// <summary>
    /// The ids the store holds for the map, when the text judged is the text it holds; none
    /// otherwise, because ids of another text could name other elements.
    /// </summary>
    private IReadOnlyList<WardleyIdentityEntry> IdentitiesOf(DiagramValidationRequest request) =>
        request.BodyPath.Length > 0 && documents.Get(request.BodyPath) is { IsUsable: true } entry && entry.Document.Text == request.Document
            ? documents.Identities(request.BodyPath)
            : [];
}
