using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Problems;

/// <summary>
/// When a remembered verdict may no longer hold: its file changed or went, or the authority
/// that drew it - the router, or the module's rules - moved since. Kept apart from the
/// <see cref="ProblemCache"/> that remembers the verdicts, because it is the one part of the
/// problem store that knows the router and the validators.
/// </summary>
internal sealed class ProblemStalenessPolicy(DiagramFileRouter router, DiagramValidators validators)
{
    /// <summary>
    /// Whether a remembered verdict may no longer hold.
    /// </summary>
    /// <remarks>
    /// The obvious half is the file: it changed, or it is gone. The half that cost this
    /// repository three defects in one night is the other one - <b>a verdict whose authority
    /// moved out from under it while its file stood still.</b> Three ways that happens, and
    /// no file stamp can see any of them, by construction: the registry GAINS a type and a
    /// core "not a known diagram type" goes on denying it; a module is released and its own
    /// verdicts describe rules that no longer judge the file; a module is REMOVED and its
    /// verdicts, and core's verdicts about it, have nothing left standing behind them. Each
    /// check below is one of those, and each was found by fixing the one before it. Adding a
    /// new kind of verdict means asking which authority it rests on and whether that
    /// authority can move - because if it can and nothing here asks, the panel will present
    /// the verdict as a claim about now.
    /// </remarks>
    public bool IsStale(string rootPath, StoredProblem problem)
    {
        // The subject may be a folder as well as a file - a rule that blames a role folder has
        // no file to blame - and FileInfo.Exists is false for one, which made every
        // folder-located problem permanently stale (found by the ansible-structure-diagram
        // manual pass).
        var fullPath = ProblemStamp.FullPathOrNull(rootPath, problem.RelativePath);
        if (fullPath is null || !ProblemStamp.Exists(fullPath))
        {
            return true;
        }

        (DateTime lastWriteTimeUtc, long length) = ProblemStamp.Of(fullPath);
        if (lastWriteTimeUtc != problem.LastWriteTimeUtc || length != problem.Length)
        {
            return true;
        }

        if (problem.RulesVersion.Length == 0)
        {
            // A core verdict, so there is no module version to compare - core is its own
            // current version. But two of them are claims about the diagram-type REGISTRY
            // rather than about the file, and the registry moves when a module is discovered:
            // "'dotnet/dependency-graph' is not a known diagram type" stops being true the
            // moment that module is built, while the .adp it names never changes. Neither the
            // file stamp nor the rules version can carry that news, so such a verdict outlived
            // a rebuild and a restart and was shown with the authority of a current one - it
            // cost three sessions an evening diagnosing a module that was working. The router
            // is the authority the verdict was drawn from, so ask it again.
            return CoreRuleIds.IsAboutRouting(problem.Problem.RuleId) && router.Route(fullPath) is DiagramRouted;
        }

        // Nothing routes the file any more, so the module that judged it is gone -
        // unregistered, uninstalled, or dropped from a build. There is no version left to
        // compare and nothing standing behind the verdict, which is precisely what stale
        // means. Gating the comparison on a successful route instead left such a verdict
        // reading fresh for ever, the mirror of the case above: there the registry GAINED a
        // type and a core verdict went on denying it; here it LOSES one and a module's
        // verdict goes on asserting it. The entry is marked rather than dropped so the
        // record of a problem that was once real survives, and a module removed by accident
        // does not silently take its findings with it.
        if (router.Route(fullPath) is not DiagramRouted routed)
        {
            return true;
        }

        // A module release invalidates its own verdicts (Requirement 4.7): compare against
        // the rules that would judge the file today.
        return !string.Equals(validators.RulesVersion(routed.Definition.Origin), problem.RulesVersion, StringComparison.Ordinal);
    }

}
