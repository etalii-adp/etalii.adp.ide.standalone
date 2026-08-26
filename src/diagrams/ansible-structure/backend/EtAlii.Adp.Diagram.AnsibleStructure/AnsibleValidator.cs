using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Adapts <see cref="AnsibleRuleSet"/> to core's validator seam, so this type's rules reach the
/// errors-and-warnings panel like any other type's.
/// </summary>
/// <remarks>
/// <para>
/// This class is the reason the seam was widened. Every other validator reads
/// <see cref="DiagramValidationRequest.Document"/>; this one cannot - for a folder-subject type
/// that document is the <c>.adp</c>'s single MIME line, which says nothing about the project.
/// It reads <see cref="DiagramValidationRequest.SubjectFolder"/> instead, which core fills only
/// for a <see cref="DiagramSubject.Folder"/> type.
/// </para>
/// <para>
/// It reads the folder itself rather than going through <see cref="IAnsibleProjectStore"/>.
/// Validation is a one-shot question, not a subscription: routing it through the store would
/// make every "Validate all" leave a watcher behind on a folder nobody has open.
/// </para>
/// </remarks>
public sealed class AnsibleValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleValidator>();

    private readonly AnsibleProjectReader _reader;

    public AnsibleValidator(AnsibleProjectReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public AnsibleValidator()
        : this(new AnsibleProjectReader())
    {
    }

    public DiagramOrigin Origin => Diagram.AnsibleStructure.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SubjectFolder is not { Length: > 0 } folder)
        {
            // Core fills SubjectFolder for a Folder-subject type, and this type is one. Reaching
            // here means the definition and the request disagree, which is a deployment fault
            // rather than a fault in anybody's Ansible - so it is logged, not reported at the
            // user as though their project were broken.
            _logger.Warning(
                "No subject folder was given for {BaseName}; this type reads a folder, so there is nothing to judge",
                request.BaseName);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
        }

        var problems = AnsibleRuleSet.Judge(_reader.Read(folder));
        return ValueTask.FromResult(Rebased(problems, request.RootPath, folder));
    }

    /// <summary>
    /// Rewrites each problem's location from folder-relative to project-relative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AnsibleRuleSet"/> works in the model's own terms, where a path is relative to
    /// the diagram's folder - <c>roles/nginx/meta/main.yml</c>. Core works in the project's,
    /// because that is what a client can be shown and what the problem store keys on. This is
    /// the one place both are known, so this is where the two meet.
    /// </para>
    /// <para>
    /// It matters only when the diagram's folder is not the project root, which is the ordinary
    /// case and was invisible to every unit test in this module - there the folder <em>is</em>
    /// the root. <c>AnsibleValidationFlowTests</c> caught it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DiagramProblem> Rebased(
        IReadOnlyList<DiagramProblem> problems, string rootPath, string folder)
    {
        if (rootPath.Length == 0)
        {
            return problems;
        }

        var prefix = IoPath.GetRelativePath(rootPath, folder).Replace('\\', '/');
        return prefix is "." or ""
            ? problems
            : [.. problems.Select(problem => problem.Location is DiagramProblemFileLocation located
                ? problem with { Location = located with { RelativePath = $"{prefix}/{located.RelativePath}" } }
                : problem)];
    }
}
