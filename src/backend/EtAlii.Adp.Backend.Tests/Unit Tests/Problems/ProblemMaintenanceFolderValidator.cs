using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A folder-subject type's validator: it reads <see cref="DiagramValidationRequest.SubjectFolder"/>
/// rather than the document, and reports one problem located in a file inside that folder -
/// which is what a real folder-subject rule set does, and what makes the maintenance loop's
/// walk-up observable.
/// </summary>
internal sealed class ProblemMaintenanceFolderValidator(DiagramOrigin origin, string locatedRelativePath) : IDiagramValidator
{
    private int _calls;

    public DiagramOrigin Origin { get; } = origin;
    public int Calls => _calls;

    /// <summary>The folder the last call was given, so a test can prove it was handed one at all.</summary>
    public string? LastSubjectFolder { get; private set; }

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastSubjectFolder = request.SubjectFolder;
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
        [
            new DiagramProblem(
                DiagramProblemSeverity.Error,
                "The role 'absent-role' has no folder.",
                "fixture.role-missing",
                new DiagramProblemFileLocation(locatedRelativePath, 3)),
        ]);
    }
}
