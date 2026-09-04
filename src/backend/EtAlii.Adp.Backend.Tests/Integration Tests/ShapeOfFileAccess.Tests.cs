using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Production code reaches for the central file helpers rather than a raw file API
/// (file-io-centralization Requirement 5.1). The class of defect this guards is not
/// hypothetical: two header helpers were written independently, after a twelve-site audit had
/// removed exactly their flaw, and each reproduced it - because nothing failed when they did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this landed before the conversions it describes.</b> The spec's headline defect was
/// fixed before implementation began, which leaves the guard as the durable half of the work.
/// A guard sequenced last is a guard that gets cut when the interesting work finishes, so this
/// one arrives first and <b>green</b>: the sites still to convert sit in
/// <see cref="Tracked"/>, each naming the task that will delete its line, and
/// <c>NoTrackedEntryOutlivesItsFix</c> refuses to let one linger after its site is clean.
/// </para>
/// <para>
/// <b>The two allow-lists are different in kind and must stay visibly so.</b>
/// <see cref="Permanent"/> is a design decision with a reason - a file that is the
/// implementation, or that touches an ADP-owned file rather than a user document.
/// <see cref="Tracked"/> is a debt with an owner. Merging them would turn "not yet" into
/// "never" silently, which is the failure this whole spec exists to prevent.
/// </para>
/// </remarks>
public partial class ShapeOfFileAccessTests
{
    /// <summary>One reach for a raw file API, with the central call that replaces it.</summary>
    private sealed record Violation(string File, int Line, string Rule, string Replacement)
    {
        public override string ToString() => $"{File}:{Line} - {Rule}. Use {Replacement}.";
    }

    /// <summary>
    /// Files exempt by design, with the reason. Never extend this to silence a real finding:
    /// a site that ought to change belongs in <see cref="Tracked"/> with a task, not here.
    /// </summary>
    private static readonly (string File, string Rule, string Reason)[] Permanent =
    [
        ("Hierarchy/SharedDocumentReader.cs", AllRules, "it is the shared-read implementation"),
        ("Hierarchy/AdpFileWriter.cs", AllRules, "it is the temp-then-move implementation"),
        ("EtAlii.Adp.Editor/TextFileBuffer.cs", AllRules, "a deliberately strict decode that must refuse a torn read rather than save it back (Requirement 2.4); its FileStream is a write, and a write does not share"),
        ("Problems/ProblemStore.cs", RawRead, "ADP's own problem cache, not a user document - no user-driven writer contends for it"),
        ("Projects/FileProjectStore.cs", RawRead, "ADP's own projects file, not a user document"),
        ("C4/C4LayoutSidecar.cs", RawRead, "reads ADP's own layout sidecar JSON, not the user's .dsl"),
        ("WardleyMap/WardleyIdentities.cs", RawRead, "reads ADP's own identities sidecar JSON, not the user's .owm"),
    ];

    /// <summary>
    /// A permanent entry excuses ONE rule unless it says otherwise. Blanket-excusing a file
    /// is what let C4LayoutSidecar's fine JSON read silently pardon its hand-rolled publish
    /// the first time this guard ran - which is exactly what
    /// <see cref="ThePermanentAndTrackedListsStaySeparate"/> is for, and it caught it.
    /// </summary>
    private const string AllRules = "*";

    /// <summary>
    /// Debt with an owner. Each line names the task that deletes it, and
    /// <c>NoTrackedEntryOutlivesItsFix</c> fails if the site is clean while the line remains -
    /// so a tracked exemption cannot quietly become a permanent one.
    /// </summary>
    /// <remarks>
    /// The three <c>FileShare</c> entries are the ones file-io-centralization's design named
    /// (F2), and they are the module counterparts to the core sites
    /// <c>backend-consistency</c> owns; neither spec claims the other's. The rest were found
    /// by this guard when it was first run, are named in no spec's task list yet, and are
    /// reported as a finding rather than absorbed - see the task 1.1 implementation log.
    /// </remarks>
    private static readonly (string File, string Rule, string Owner)[] Tracked =
    [
        // Reads of a user document at FileShare.Read, the original flaw.
        ("Databricks/DatabricksDocumentStore.cs", RawRead, "unassigned - reported by task 1.1"),
        ("Rdf/RdfDocumentStore.cs", RawRead, "unassigned - reported by task 1.1"),

        // Core reads of a user document - owned by backend-consistency, not by this spec.
        ("Hierarchy/AddDiagramContextActionProvider.cs", RawRead, "backend-consistency AC1"),
        ("Hierarchy/RegistrationLayout.cs", RawRead, "backend-consistency AC2"),

        // Shared reads that omit FileShare.Delete - file-io-centralization tasks 3.1-3.3.
        ("AnsibleStructure/AnsibleYaml.cs", NarrowShare, "file-io-centralization task 3.1"),
        ("HelmCharts/HelmYaml.cs", NarrowShare, "file-io-centralization task 3.2"),
        ("HelmCharts/HelmChartReader.cs", NarrowShare, "file-io-centralization task 3.3"),

        // Hand-rolled temp-then-move publishes.
        ("Mindmap/MindmapDocumentStore.cs", HandRolledPublish, "unassigned - reported by task 1.1"),
        ("WardleyMap/WardleyDocumentStore.cs", HandRolledPublish, "unassigned - reported by task 1.1"),
        ("C4/C4LayoutSidecar.cs", HandRolledPublish, "unassigned - reported by task 1.1"),
        ("WardleyMap/WardleyIdentities.cs", HandRolledPublish, "unassigned - reported by task 1.1"),
    ];

    private const string RawRead = "reads a file with a raw File.ReadAllText/ReadAllLines, which opens at FileShare.Read and loses to a concurrent save";
    private const string NarrowShare = "opens a read stream without FileShare.Delete, so ADP's own temp-then-move publish cannot replace the file mid-read";
    private const string HandRolledPublish = "publishes with a hand-rolled temporary and File.Move instead of the central atomic writer";

    private static readonly string Replacement = "SharedDocumentReader (reads) or AdpFileWriter (publishes)";

    private static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "src", "backend")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/diagrams beside src/backend) was not found above the test binary.");
    }

    /// <summary>Production C# only: generated output, build artifacts and test projects are not the subject.</summary>
    private static IEnumerable<string> ProductionSources() =>
        Directory.EnumerateFiles(IoPath.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains(".Tests", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal));

    /// <summary>
    /// The rules, as a pure function over one file's lines so they can be exercised directly
    /// rather than only through the tree - which is what makes the sabotage test below
    /// possible without planting a file in the repository.
    /// </summary>
    internal static IReadOnlyList<string> Offences(string relativePath, IReadOnlyList<string> lines)
    {
        var found = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("///", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("*", StringComparison.Ordinal))
            {
                continue; // prose about a rule is not a breach of it
            }

            if (RawReadExpression().IsMatch(line))
            {
                found.Add(new Violation(relativePath, i + 1, RawRead, Replacement).ToString());
            }

            if (ReadStreamExpression().IsMatch(line) && !line.Contains("FileShare.Delete", StringComparison.Ordinal))
            {
                found.Add(new Violation(relativePath, i + 1, NarrowShare, Replacement).ToString());
            }

            // A publish is a move OUT of a temporary; a rename moves a real path to a real path
            // and is a different operation, so the temporary is what identifies this.
            if (line.Contains("File.Move(", StringComparison.Ordinal) &&
                line.Contains("temporar", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new Violation(relativePath, i + 1, HandRolledPublish, Replacement).ToString());
            }
        }

        return found;
    }

    private static bool IsPermitted(string relativePath, string rule) =>
        Permanent.Any(entry =>
            relativePath.EndsWith(entry.File.Replace('/', IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            (entry.Rule == AllRules || entry.Rule == rule)) ||
        Tracked.Any(entry =>
            relativePath.EndsWith(entry.File.Replace('/', IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            entry.Rule == rule);

    /// <summary>
    /// The whole survey, walked once per test run rather than once per call.
    /// </summary>
    /// <remarks>
    /// Memoised because the first version was not, and one test called it inside a LINQ
    /// predicate - so the tree was walked once per tracked entry, roughly fourteen full
    /// walks of four thousand files. That did not fail this guard; it destabilised the
    /// integration tests sharing the assembly, surfacing a real race between a watcher-driven
    /// read of an .adp and the layout write that undo performs. The race is genuine and is
    /// backend-consistency AC2 to fix. Making this guard cheap is what stops it acting as a
    /// load generator that decides when that race is lost.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<(string RelativePath, string Offence, string Rule)>> Surveyed =
        new(WalkTheTree, LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyList<(string RelativePath, string Offence, string Rule)> Survey() => Surveyed.Value;

    private static IReadOnlyList<(string RelativePath, string Offence, string Rule)> WalkTheTree()
    {
        var all = new List<(string, string, string)>();
        foreach (var file in ProductionSources())
        {
            var relative = IoPath.GetRelativePath(RepositoryRoot, file);
            var lines = File.ReadAllLines(file);
            foreach (var offence in Offences(relative, lines))
            {
                var rule = offence.Contains(RawRead, StringComparison.Ordinal) ? RawRead
                    : offence.Contains(NarrowShare, StringComparison.Ordinal) ? NarrowShare
                    : HandRolledPublish;
                all.Add((relative, offence, rule));
            }
        }

        return all;
    }

    [Fact]
    public void NoProductionFileReachesForARawFileApi()
    {
        // Act.
        var unexcused = Survey()
            .Where(entry => !IsPermitted(entry.RelativePath, entry.Rule))
            .Select(entry => entry.Offence)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        // The message is the deliverable: whoever hits this needs the file, the line and the
        // call to use, not a count.
        Assert.True(
            unexcused.Length == 0,
            "These reach for a raw file API where a central helper exists:" +
            Environment.NewLine + string.Join(Environment.NewLine, unexcused));
    }

    [Fact]
    public void NoTrackedEntryOutlivesItsFix()
    {
        // Arrange.
        // A tracked exemption is debt with an owner. Once its site is clean the line must go,
        // or "not yet" quietly becomes "never" - which is the whole failure this spec is about.
        var live = Survey();

        // Act.
        var stale = Tracked
            .Where(entry => !live.Any(found =>
                found.RelativePath.EndsWith(entry.File.Replace('/', IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                found.Rule == entry.Rule))
            .Select(entry => $"{entry.File} ({entry.Owner}) is tracked but no longer offends - delete its line")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        Assert.True(stale.Length == 0, string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void ThePermanentAndTrackedListsStaySeparate()
    {
        var live = Survey();

        // A file may appear in both only where the two entries are about different rules -
        // C4LayoutSidecar reads ADP's own JSON (permanently fine) and publishes by hand
        // (tracked debt). Same file, same rule, both lists would mean the debt was quietly
        // pardoned, so that is what this refuses.
        var pardoned = Tracked
            .Where(tracked => Permanent.Any(permanent =>
                permanent.File.Equals(tracked.File, StringComparison.OrdinalIgnoreCase) &&
                (permanent.Rule == AllRules || permanent.Rule == tracked.Rule)))
                        .Where(tracked => live.Any(found =>
                found.RelativePath.EndsWith(tracked.File.Replace('/', IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                found.Rule == tracked.Rule))
            .Select(tracked => $"{tracked.File} is tracked for a rule its permanent entry already excuses")
            .ToArray();

        Assert.True(pardoned.Length == 0, string.Join(Environment.NewLine, pardoned));
    }

    [Fact]
    public void TheGuardCatchesEachShapeItClaimsTo()
    {
        // The sabotage check: a guard nobody has watched fail is a guard nobody should trust.
        // Run against synthetic lines rather than a planted file, so the repository is never
        // left holding a deliberate defect.
        var rawRead = Offences("Fake/Store.cs", ["        var text = File.ReadAllText(path);"]);
        Assert.Single(rawRead);
        Assert.Contains("Fake/Store.cs:1", rawRead[0], StringComparison.Ordinal);
        Assert.Contains("SharedDocumentReader", rawRead[0], StringComparison.Ordinal);

        var narrow = Offences("Fake/Reader.cs", ["        using var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);"]);
        Assert.Single(narrow);
        Assert.Contains("FileShare.Delete", narrow[0], StringComparison.Ordinal);

        var publish = Offences("Fake/Writer.cs", ["            File.Move(temporary, path, overwrite: true);"]);
        Assert.Single(publish);
        Assert.Contains("AdpFileWriter", publish[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardDoesNotFireOnWhatIsGenuinelyFine()
    {
        // The other half of trust: a guard that fires on correct code gets disabled.
        Assert.Empty(Offences("Fake/Good.cs", ["        using var reader = SharedDocumentReader.OpenText(path);"]));
        Assert.Empty(Offences("Fake/Shared.cs", ["        new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);"]));
        Assert.Empty(Offences("Fake/Rename.cs", ["                File.Move(sourcePath, targetPath);"]));
        Assert.Empty(Offences("Fake/Prose.cs", ["        /// File.ReadAllText and friends open with FileShare.Read, which is why this exists."]));
        Assert.Empty(Offences("Fake/Write.cs", ["            await using var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);"]));
    }

    /// <summary>A raw whole-file read: <c>File.ReadAllText</c> or <c>File.ReadAllLines</c>.</summary>
    [GeneratedRegex(@"\bFile\.ReadAll(Text|Lines)\s*\(")]
    private static partial Regex RawReadExpression();

    /// <summary>A stream opened for reading; the sharing flags are checked separately.</summary>
    [GeneratedRegex(@"new FileStream\([^)]*FileAccess\.Read\b")]
    private static partial Regex ReadStreamExpression();
}
