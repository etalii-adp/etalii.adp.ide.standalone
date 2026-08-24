using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// One remembered problem: what a validator (or the router) reported, pinned to the file and
/// the rules that produced it, so a later look can tell whether the verdict still holds.
/// </summary>
/// <param name="Problem">What is wrong, as the diagram type reported it.</param>
/// <param name="RelativePath">The file's path relative to the project root - never absolute.</param>
/// <param name="LastWriteTimeUtc">The file's last write time when this was produced.</param>
/// <param name="Length">The file's length in bytes when this was produced.</param>
/// <param name="RulesVersion">
/// The <see cref="DiagramValidators.RulesVersion"/> that judged the file, so a module release
/// invalidates only its own cached verdicts.
/// </param>
public sealed record StoredProblem(
    DiagramProblem Problem,
    string RelativePath,
    DateTime LastWriteTimeUtc,
    long Length,
    string RulesVersion);
