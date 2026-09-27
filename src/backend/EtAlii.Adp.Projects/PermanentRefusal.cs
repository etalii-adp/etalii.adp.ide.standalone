using Grpc.Core;

namespace EtAlii.Adp.Projects;

/// <summary>
/// The status codes core answers with when it refuses to open or watch something for good - the one
/// place they are named (backend-centralization R13.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Permanent means asking again gets the same answer.</b> A client's stream loop re-opens a stream
/// that dropped, and must not re-open one that was refused, or a diagram that cannot be opened retries
/// forever instead of saying so. So every refusal <c>DiagramService</c>, <c>ContextService</c> and
/// <c>HierarchyService</c> raise uses a name from here, and <c>src/fixtures/cross-tier/permanent-statuses.json</c>
/// states the same list for the client, checked against <see cref="All"/> by the backend suite.
/// </para>
/// <para>
/// It lives in <c>EtAlii.Adp.Projects</c> because that is the one project all three services
/// reference, and <see cref="ProjectRootResolver"/>'s answer is the most common refusal they raise.
/// </para>
/// </remarks>
public static class PermanentRefusal
{
    /// <summary>
    /// What was asked for exists in no form this server can open: the project does not resolve, the
    /// file is outside it, nothing claims it, or rival editors claim it and none is the default.
    /// </summary>
    public const StatusCode CannotOpen = StatusCode.FailedPrecondition;

    /// <summary>
    /// What was asked for is recognised, but the module or editor that would open it is not deployed.
    /// </summary>
    public const StatusCode NotDeployed = StatusCode.Unimplemented;

    /// <summary>Every permanent refusal, in the order declared above.</summary>
    public static readonly IReadOnlyList<StatusCode> All = [CannotOpen, NotDeployed];

    /// <summary>Whether a stream that ended with <paramref name="code"/> will be refused again when re-opened.</summary>
    public static bool IsPermanent(StatusCode code) => All.Contains(code);
}
