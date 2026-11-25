using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Links;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Judges a dependency graph document, through the seam every diagram type's rules reach the
/// Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// Text in, problems out: parse, and hand the model to <see cref="DependencyGraphFindings"/>. A
/// file that will not parse is reported as one <b>error</b> naming the line - the same message
/// the unavailable state shows, so the panel and the canvas never disagree - because running
/// rules over a model that is empty only because the parse failed would bury that one fact under
/// a list of consequences.
/// </remarks>
public sealed class DependencyGraphValidator : IDiagramValidator
{
    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "dependencies.unparseable";

    private readonly ILinkIndex? _links;

    /// <param name="links">The index the diagram's links resolve in (FBL 17.6); none reports only the links that do not parse.</param>
    public DependencyGraphValidator(ILinkIndex? links = null)
    {
        _links = links;
    }

    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var body = DependencyGraphBody.Parse(request.Document);
        IReadOnlyList<DiagramProblem> problems = [.. DependencyGraphFindings.Judge(body), .. DislLinkFindings.Of(body.Disl.Diagram, _links?.WorkspaceOf(request.BodyPath))];
        return ValueTask.FromResult(problems);
    }
}
