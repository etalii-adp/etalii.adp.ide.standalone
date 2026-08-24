namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// What is currently known to be wrong per project, and what survives a restart. One set
/// per project root; every mutation raises <see cref="Changed"/> so the broadcaster can
/// tell every connection in that project (Requirements 1.2, 4, 5.2-5.3).
/// </summary>
public interface IProblemStore
{
    /// <summary>
    /// The project's current set, with each entry's staleness computed against the file as
    /// it is now (Requirement 4.4). A project never validated answers an empty
    /// <see cref="ProblemSetState.NeverValidated"/> set - which is not the same as clean.
    /// </summary>
    ProjectProblemSet Get(string rootPath);

    /// <summary>The whole project was validated: this is everything (Validate all).</summary>
    void Replace(string rootPath, IReadOnlyList<StoredProblem> problems);

    /// <summary>
    /// Only <paramref name="relativePaths"/> were validated - one file, or a folder and
    /// everything beneath it: their old entries go, <paramref name="problems"/> come, and
    /// the rest of the set stands.
    /// </summary>
    void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems);

    /// <summary>The file (or folder) is gone; so are its problems (Requirement 5.2).</summary>
    void Remove(string rootPath, string relativePath);

    /// <summary>
    /// The file (or folder) was renamed or moved: its problems follow it rather than being
    /// re-reported as unchecked (Requirement 5.3).
    /// </summary>
    void Move(string rootPath, string fromRelativePath, string toRelativePath);

    /// <summary>A project's set changed; the payload is its root path.</summary>
    event Action<string>? Changed;
}
