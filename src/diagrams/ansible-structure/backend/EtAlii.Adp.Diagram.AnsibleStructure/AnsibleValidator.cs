using Serilog;

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

        return ValueTask.FromResult(AnsibleRuleSet.Judge(_reader.Read(folder)));
    }
}
