namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The stand-in for what FBL has no construct for (the companion's language gaps 1 and 2, findings B2, B4 and B6):
/// a subject that is one file, the solution, with the files it leads to. It is the only class that knows where
/// the solution is on disk, and the plugin reaches every file beyond the body through it: the project files the
/// solution names, the <c>Directory.Packages.props</c> files above them, and the local NuGet cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>The library never learns a path</b> (Requirement 7.3): the module makes one subject per body and hands it to
/// the plugin's constructor, and <c>PluginReadRequest</c> stays the body's bytes. So when FBL has a subject that
/// follows references (a <c>resolve</c> operation, or its equal) and a declared environment root, the host hands
/// those files over instead, and this class is what goes.
/// </para>
/// <para>
/// <b>It opens the files the way the readers always have</b> (Q1, Q3): through <see cref="ProjectReader"/>, which
/// walks up for the versions files to the root of the drive, and <see cref="PackageDescriptionReader"/>, which reads
/// the machine's cache, without asking. Nothing here writes.
/// </para>
/// </remarks>
public sealed class DotNetSolutionSubject
{
    private readonly SolutionReader _solutions;
    private readonly ProjectReader _projects;
    private readonly PackageDescriptionReader _descriptions;

    /// <summary>The subject of the solution at <paramref name="solutionPath"/>, read by the given readers.</summary>
    public DotNetSolutionSubject(string solutionPath, SolutionReader solutions, ProjectReader projects, PackageDescriptionReader descriptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        SolutionPath = Path.GetFullPath(solutionPath);
        _solutions = solutions ?? throw new ArgumentNullException(nameof(solutions));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _descriptions = descriptions ?? throw new ArgumentNullException(nameof(descriptions));
    }

    /// <summary>The solution's full path: the body's.</summary>
    public string SolutionPath { get; }

    /// <summary>The solution of <paramref name="text"/>, the body as the host read it, with its projects resolved on disk.</summary>
    public SolutionReading Solution(string text) => _solutions.Read(SolutionPath, text);

    /// <summary>The project file at <paramref name="absolutePath"/>, which the solution named.</summary>
    public ProjectReading Project(string absolutePath) => _projects.Read(absolutePath);

    /// <summary>A package's description from the local cache, or null where the cache has none.</summary>
    public string? Description(string packageId, IReadOnlyList<string> versions) => _descriptions.Read(packageId, versions);
}
