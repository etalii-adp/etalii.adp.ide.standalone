namespace EtAlii.Adp.History;

/// <summary>
/// Where a command's warning goes: something that happened alongside an edit that
/// <b>succeeded</b>, and that the user would otherwise discover only later.
/// </summary>
/// <remarks>
/// <para>
/// A one-method seam rather than a direct dependency on the context stream, so the history
/// stack does not have to know how a project's watchers are reached - and so a test can watch
/// what would have been said without standing up a stream.
/// </para>
/// <para>
/// History defines it and names it for what it carries, a command's notice; the context
/// stream implements it. So the dependency runs one way, Context on History, and nothing in
/// History is named after the project that depends on it (code-structure FR-064).
/// </para>
/// <para>
/// It sits here, at the one place every command's result passes through, rather than in each
/// module. A warning threaded back through <c>IDiagramSession</c> and every response message
/// would have to be added to every module that exists and remembered by every module written
/// afterwards; this is added once and covers all of them.
/// </para>
/// </remarks>
public interface ICommandNoticeSink
{
    /// <summary>Tells everyone watching the project at <paramref name="root"/>.</summary>
    void Notify(ProjectRoot root, string message);
}
