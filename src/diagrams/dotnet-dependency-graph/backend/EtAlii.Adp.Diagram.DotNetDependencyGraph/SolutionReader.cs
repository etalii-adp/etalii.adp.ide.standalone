using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Reads a solution file and yields the project files it names. Both serializations of the
/// same thing: the classic <c>.sln</c> with its <c>Project(...)</c> lines, and the newer
/// <c>.slnx</c> XML.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is fatal and nothing here is silent</b> (Requirement 2.4). An unreadable
/// solution yields no projects and one failure; a project the solution names but which is not
/// on disk is omitted from the projects and named in the failures. The caller builds the graph
/// from what resolved and reports what did not - so a solution mid-rename opens showing the
/// projects that are still there, rather than opening empty or not opening at all.
/// </para>
/// <para>
/// <b>This reader never writes.</b> The one file this module writes is its own <c>.adp</c>, and
/// only that file's <c>layout:</c> block.
/// </para>
/// </remarks>
public sealed class SolutionReader
{
    private static readonly ILogger _logger = Log.ForContext<SolutionReader>();

    /// <summary>
    /// The type GUID a classic <c>.sln</c> gives a <b>solution folder</b> - an organisational
    /// node, not a project. Its second field is a folder name rather than a path to a file, so
    /// a reader that took every <c>Project(...)</c> line at face value would emit one node per
    /// solution folder and then report each as a missing project file. Compared
    /// case-insensitively because the casing in the file is the writing tool's choice.
    /// </summary>
    private const string SolutionFolderTypeGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>
    /// <c>Project("{type}") = "name", "relative\path.csproj", "{id}"</c> - one line, four fields,
    /// of which this module wants the type (to reject folders) and the path.
    /// </summary>
    private static readonly Regex _classicProjectLine = new(
        """^\s*Project\("\{(?<type>[^}]*)\}"\)\s*=\s*"(?<name>[^"]*)"\s*,\s*"(?<path>[^"]*)"\s*,\s*"\{(?<id>[^}]*)\}"\s*$""",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Reads <paramref name="solutionPath"/>; never throws for a bad solution.</summary>
    public SolutionReading Read(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        if (!File.Exists(solutionPath))
        {
            return Absent(solutionPath);
        }

        string text;
        try
        {
            // SharedDocumentReader rather than File.ReadAllText: the raw API opens at
            // FileShare.Read and loses to a concurrent save, so a solution being written by an
            // IDE at the moment the diagram opens would read as unreadable. ShapeOfFileAccess
            // caught this - the guard is real, and it was right.
            text = SharedDocumentReader.ReadAllText(solutionPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Locked by a build, or unreadable by this account. A diagram that cannot be drawn
            // is still a diagram that opens, with the reason on it.
            return Unreadable(solutionPath, error);
        }

        return Read(solutionPath, text);
    }

    /// <summary>
    /// Reads the solution at <paramref name="solutionPath"/> from <paramref name="text"/>, its content already read,
    /// so that the file is read once: by the host, which hands its bytes to the plugin. The path still decides the
    /// serialization, by its extension, and is where the projects it names are resolved from. Never throws for a
    /// bad solution.
    /// </summary>
    public SolutionReading Read(string solutionPath, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(text);

        var extension = Path.GetExtension(solutionPath);
        if (string.Equals(extension, Diagram.AlternateDocumentExtension, StringComparison.OrdinalIgnoreCase))
        {
            var parsed = ReadSlnx(text, solutionPath);

            // Reported, never swallowed (Requirement 2.4). Returning no projects AND no reason
            // would open an empty diagram claiming the solution contains nothing - which is the
            // failure mode the requirement names rather than a mild version of it. Found by the
            // session's integration test, after this reader's own unit test asserted only that
            // no projects came back and so passed over the missing reason.
            return parsed is null
                ? SolutionReading.OfFailure(solutionPath, "The solution file could not be parsed.")
                : Resolve(parsed, solutionPath);
        }

        return Resolve(ReadClassic(text), solutionPath);
    }

    /// <summary>The reading of a solution that does not exist: no projects, and today's sentence.</summary>
    internal static SolutionReading Absent(string solutionPath)
    {
        _logger.Warning("Solution {Solution} does not exist", solutionPath);
        return SolutionReading.OfFailure(solutionPath, "The solution file does not exist.");
    }

    /// <summary>The reading of a solution that could not be read: no projects, and the reason.</summary>
    internal static SolutionReading Unreadable(string solutionPath, Exception error)
    {
        _logger.Warning(error, "Solution {Solution} could not be read", solutionPath);
        return SolutionReading.OfFailure(solutionPath, $"The solution file could not be read: {error.Message}");
    }

    /// <summary>
    /// The XML serialization: <c>Project Path=</c>, at any depth inside folders. <c>null</c>
    /// when the document will not parse - distinct from an empty list, which is a solution that
    /// genuinely names no projects.
    /// </summary>
    private static IReadOnlyList<DeclaredProject>? ReadSlnx(string text, string solutionPath)
    {
        XDocument document;
        try
        {
            // With line information, so that a project's source is its entry (Q6).
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException error)
        {
            _logger.Warning(error, "Solution {Solution} is not well-formed XML", solutionPath);
            return null;
        }

        // Descendants rather than children: a .slnx nests projects inside <Folder> elements,
        // and the folder a project is filed under is presentation rather than structure. Only
        // <Project> is taken - a <Folder> also carries <File> entries (this repository files
        // its .editorconfig and Directory.Packages.props that way) and those are not projects.
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => (Element: element, Path: (string?)element.Attribute("Path")))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Select(entry =>
            {
                (int line, int column) = EntrySpan.PositionOf(entry.Element);
                return new DeclaredProject(entry.Path!, line, column);
            })
            .ToArray();
    }

    /// <summary>The classic serialization: one <c>Project(...)</c> line each, folders included.</summary>
    private static IReadOnlyList<DeclaredProject> ReadClassic(string text)
    {
        var declared = new List<DeclaredProject>();
        foreach (Match match in _classicProjectLine.Matches(text))
        {
            var type = match.Groups["type"].Value;
            if (string.Equals(type, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
            {
                // A solution folder's "path" is its display name. Skipped here rather than
                // resolved and then reported missing, which would turn every folder in a
                // solution into a problem the user cannot act on.
                continue;
            }

            declared.Add(new DeclaredProject(match.Groups["path"].Value, LineOf(text, match.Index), 0));
        }

        return declared;
    }

    /// <summary>
    /// Turns declared paths into projects on disk, relative to the solution. A path that
    /// resolves to nothing becomes a failure naming it, never a silently dropped project.
    /// </summary>
    private static SolutionReading Resolve(IReadOnlyList<DeclaredProject> declared, string solutionPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? string.Empty;
        var projects = new List<SolutionProject>();
        var failures = new List<SolutionFailure>();

        foreach ((string relative, int line, int column) in declared)
        {
            // Solution files write Windows separators whatever the platform reading them, so
            // the separator is normalised rather than trusted.
            var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var absolute = Path.GetFullPath(Path.Combine(directory, normalized));

            if (!File.Exists(absolute))
            {
                failures.Add(new SolutionFailure(
                    relative,
                    "The solution names this project, but the file is not there."));
                continue;
            }

            projects.Add(new SolutionProject(
                Path.GetFileNameWithoutExtension(absolute),
                RelativePathOf(directory, absolute),
                absolute)
            {
                Line = line,
                Column = column,
            });
        }

        _logger.Information(
            "Solution {Solution} named {Declared} projects; {Resolved} resolved, {Failed} did not",
            solutionPath,
            declared.Count.ToString(CultureInfo.InvariantCulture),
            projects.Count.ToString(CultureInfo.InvariantCulture),
            failures.Count.ToString(CultureInfo.InvariantCulture));

        return new SolutionReading(projects, failures);
    }

    /// <summary>
    /// The project's path relative to the solution, forward-slashed. This is what an element id
    /// is built from, so it is written one way on every platform: an id that read
    /// <c>project:src\a\a.csproj</c> on Windows and <c>project:src/a/a.csproj</c> elsewhere
    /// would lose a stored position on the way between them.
    /// </summary>
    private static string RelativePathOf(string solutionDirectory, string absoluteProjectPath) =>
        Path.GetRelativePath(solutionDirectory, absoluteProjectPath).Replace('\\', '/');

    /// <summary>The 1-based line of the character at <paramref name="index"/>: one more than the line feeds before it.</summary>
    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    /// <summary>A project as the solution declares it: the path as written, and where its entry is.</summary>
    private readonly record struct DeclaredProject(string Path, int Line, int Column);
}
