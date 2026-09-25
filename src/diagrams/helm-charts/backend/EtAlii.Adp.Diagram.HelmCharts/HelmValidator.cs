using EtAlii.Adp.Documents;
using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Adapts <see cref="HelmRuleSet"/> to core's validator seam, so this type's rules reach the
/// errors-and-warnings panel like any other type's (Requirement 10.8).
/// </summary>
/// <remarks>
/// <para>
/// A folder-subject validator reads <see cref="DiagramValidationRequest.SubjectFolder"/> - the
/// seam the first folder-subject module widened - because for this type the document is the
/// <c>.adp</c>'s single MIME line, which says nothing about the chart.
/// </para>
/// <para>
/// It reads the folder itself rather than going through <see cref="IHelmChartStore"/>.
/// Validation is a one-shot question, not a subscription: routing it through the store would
/// make every "Validate all" leave a watcher behind on a folder nobody has open.
/// </para>
/// </remarks>
public sealed class HelmValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<HelmValidator>();

    private readonly HelmChartReader _reader;

    public HelmValidator(HelmChartReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public HelmValidator()
        : this(new HelmChartReader())
    {
    }

    public DiagramOrigin Origin => Diagram.HelmCharts.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SubjectFolder is not { Length: > 0 } folder)
        {
            // Core fills SubjectFolder for a Folder-subject type, and this type is one.
            // Reaching here means the definition and the request disagree - a deployment
            // fault, not a fault in anybody's chart - so it is logged, not reported at the
            // user as though their chart were broken.
            _logger.Warning(
                "No subject folder was given for {BaseName}; this type reads a folder, so there is nothing to judge",
                request.BaseName);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
        }

        var problems = HelmRuleSet.Judge(_reader.Read(folder));
        return ValueTask.FromResult(Rebased(problems, request.RootPath, folder));
    }

    /// <summary>
    /// Rewrites each problem's location from chart-root-relative to project-relative - the one
    /// place both are known. It matters only when the chart is not the project root, which is
    /// the ordinary case and was invisible to every module unit test in the sibling module
    /// (its integration flow caught it); the tests here cover the nested chart directly.
    /// </summary>
    /// <remarks>
    /// The <c>.</c> location - the rule set's way of blaming the chart folder itself
    /// (Requirement 10.1) - rebases to the folder's own project-relative path, never to a
    /// literal <c>prefix/.</c>.
    /// </remarks>
    private static IReadOnlyList<DiagramProblem> Rebased(
        IReadOnlyList<DiagramProblem> problems, string rootPath, string folder)
    {
        if (rootPath.Length == 0)
        {
            return problems;
        }

        var prefix = IoPath.GetRelativePath(rootPath, folder).Replace('\\', '/');
        if (prefix is "." or "")
        {
            return problems;
        }

        return [.. problems.Select(problem => problem.Location is DiagramProblemFileLocation located
            ? problem with
            {
                Location = located with
                {
                    RelativePath = located.RelativePath is "." or ""
                        ? prefix
                        : $"{prefix}/{located.RelativePath}",
                },
            }
            : problem)];
    }
}
