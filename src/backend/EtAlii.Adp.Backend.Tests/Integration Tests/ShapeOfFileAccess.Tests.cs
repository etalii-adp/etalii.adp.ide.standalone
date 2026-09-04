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
        // TextFileBuffer.cs had an all-rules entry here until task 6.2, and it is deliberately
        // gone rather than narrowed. The reason it carried - a strict decode that must refuse a
        // torn read rather than save it back (Requirement 2.4) - never justified its SHARING,
        // because strictness and sharing are orthogonal: the decode happens after the bytes
        // arrive and says nothing about who may hold the file meanwhile. Its read now opens at
        // FileShare.ReadWrite | Delete and its decode is as strict as it ever was, so no rule
        // fires and no exemption is owed. Its write was never caught either way: the narrow-share
        // rule matches FileAccess.Read, and a write is FileAccess.Write.
        //
        // That file was the guard's blind spot twice over - the only File.ReadAllBytes in the
        // backend, which the rule did not match until 6.1, inside the only file exempted from
        // every rule. Neither exemption survives.
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
    /// The three <c>FileShare</c> entries file-io-centralization's design named (F2) are gone,
    /// deleted by tasks 3.1-3.3 as intended; the whole <c>NarrowShare</c> category is empty and
    /// its heading went with its last entry, since a heading describing an empty category
    /// misleads. What remains: two core reads owned by <c>backend-consistency</c>, which this
    /// spec does not claim. The four hand-rolled publishes that stood here were converted by
    /// tasks 5.1-5.3 and their lines deleted with the conversions; the category is empty and its
    /// heading went with its last entry, as the read categories did before it. Those four were
    /// found by this guard when it was first run rather than by any survey, and were reported as
    /// a finding rather than absorbed - see the task 1.1 implementation log.
    /// </remarks>
    private static readonly (string File, string Rule, string Owner)[] Tracked =
    [
        // Core reads of a user document - owned by backend-consistency, not by this spec.
        ("Hierarchy/AddDiagramContextActionProvider.cs", RawRead, "backend-consistency AC1"),
        ("Hierarchy/RegistrationLayout.cs", RawRead, "backend-consistency AC2"),
    ];

    private const string RawRead = "reads a file with a raw File.ReadAllText/ReadAllLines/ReadAllBytes, which opens at FileShare.Read and loses to a concurrent save";
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

    /// <summary>
    /// The unexcused-offence decision, over supplied lists rather than the real ones.
    /// </summary>
    /// <remarks>
    /// Extracted in task 7.1 so the property the whole sequencing rests on can be asserted
    /// rather than remembered. Three agents each watched this guard fail both ways on a real
    /// conversion and wrote it up in a commit message - which is evidence that does not re-run.
    /// A pure function over synthetic inputs does.
    /// </remarks>
    internal static IReadOnlyList<string> Unexcused(
        IReadOnlyList<(string RelativePath, string Offence, string Rule)> found,
        IReadOnlyList<(string File, string Rule, string Reason)> permanent,
        IReadOnlyList<(string File, string Rule, string Owner)> tracked) =>
        found
            .Where(entry => !Excuses(entry.RelativePath, entry.Rule, permanent, tracked))
            .Select(entry => entry.Offence)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The stale-entry decision, over supplied lists rather than the real ones. A tracked entry
    /// whose site no longer offends must be reported, or "not yet" quietly becomes "never".
    /// </summary>
    internal static IReadOnlyList<string> Stale(
        IReadOnlyList<(string RelativePath, string Offence, string Rule)> found,
        IReadOnlyList<(string File, string Rule, string Owner)> tracked) =>
        tracked
            .Where(entry => !found.Any(hit => Matches(hit.RelativePath, entry.File) && hit.Rule == entry.Rule))
            .Select(entry => $"{entry.File} ({entry.Owner}) is tracked but no longer offends - delete its line")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool Matches(string relativePath, string entryFile) =>
        relativePath.EndsWith(entryFile.Replace('/', IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool Excuses(
        string relativePath,
        string rule,
        IReadOnlyList<(string File, string Rule, string Reason)> permanent,
        IReadOnlyList<(string File, string Rule, string Owner)> tracked) =>
        permanent.Any(entry => Matches(relativePath, entry.File) && (entry.Rule == AllRules || entry.Rule == rule)) ||
        tracked.Any(entry => Matches(relativePath, entry.File) && entry.Rule == rule);

    [Fact]
    public void NoProductionFileReachesForARawFileApi()
    {
        // Act.
        var unexcused = Unexcused(Survey(), Permanent, Tracked);

        // Assert.
        // The message is the deliverable: whoever hits this needs the file, the line and the
        // call to use, not a count.
        Assert.True(
            unexcused.Count == 0,
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
        var stale = Stale(live, Tracked);

        // Assert.
        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
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
    public void TheAllowListDecisionsHoldInBothDirections()
    {
        // The property the whole sequencing rests on, over synthetic lists so it re-runs rather
        // than living in three commit messages. Both directions matter and they fail differently:
        // one lets a conversion land while its exemption silently stays, the other lets an
        // exemption be deleted while the code still offends.
        // The survey yields OS-separator relative paths, so the synthetic one must too. The
        // first draft of this test wrote a forward slash and failed on Windows for that reason
        // alone - the same lesson in miniature: a synthetic input shaped unlike the real one
        // tests something else.
        var path = IoPath.Combine("Fake", "Store.cs");
        var offence = (path, $"{path}:1 - raw read", RawRead);
        var found = new[] { offence };
        var empty = Array.Empty<(string, string, string)>();

        var permanentNone = Array.Empty<(string File, string Rule, string Reason)>();
        var trackedNone = Array.Empty<(string File, string Rule, string Owner)>();
        var trackedIt = new[] { ("Fake/Store.cs", RawRead, "some task") };

        // Direction one - code offends, nothing excuses it: reported.
        Assert.Single(Unexcused(found, permanentNone, trackedNone));

        // Direction two - code offends and a tracked entry owns it: not reported, and not stale.
        Assert.Empty(Unexcused(found, permanentNone, trackedIt));
        Assert.Empty(Stale(found, trackedIt));

        // Direction three - the site is clean but its tracked line remains: reported as stale.
        var stale = Stale(empty, trackedIt);
        Assert.Single(stale);
        Assert.Contains("no longer offends", stale[0], StringComparison.Ordinal);
        Assert.Contains("some task", stale[0], StringComparison.Ordinal);

        // Direction four - the exemption is deleted while the code still offends: reported again.
        // Together with direction two this is the pincer: neither half can be dropped quietly.
        Assert.Single(Unexcused(found, permanentNone, trackedNone));

        // A tracked entry excuses ONE rule, never the file. An entry for a different rule must
        // not pardon this offence - the failure that let a fine JSON read excuse a hand-rolled
        // publish the first time this guard ran.
        Assert.Single(Unexcused(found, permanentNone, [("Fake/Store.cs", HandRolledPublish, "other task")]));

        // A permanent all-rules entry does excuse the file, which is exactly why so few exist.
        Assert.Empty(Unexcused(found, [("Fake/Store.cs", AllRules, "it is the implementation")], trackedNone));
    }

    [Fact]
    public void TheTrackedListHoldsNothingThisSpecStillOwes()
    {
        // file-io-centralization's own debt is discharged: groups 3 and 5 deleted every line
        // that named one of its tasks. What may remain is another spec's, and saying so here
        // means a new tracked entry cannot be added under this spec's name and forgotten -
        // the failure mode task 1.1 named when it created the list.
        var mine = Tracked
            .Where(entry => entry.Owner.Contains("file-io-centralization", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.File} still names this spec ({entry.Owner})")
            .ToArray();

        Assert.True(mine.Length == 0, string.Join(Environment.NewLine, mine));

        // And every survivor carries an owner that is somebody, not a shrug.
        var ownerless = Tracked
            .Where(entry => string.IsNullOrWhiteSpace(entry.Owner) ||
                            entry.Owner.Contains("unassigned", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.File} has no owner - a tracked entry without one is a permanent entry in disguise")
            .ToArray();

        Assert.True(ownerless.Length == 0, string.Join(Environment.NewLine, ownerless));
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

        // Each ReadAll shape separately, because the rule is an alternation and an alternation
        // is exactly where one branch can be dropped without the others noticing (task 6.1).
        var rawLines = Offences("Fake/Lines.cs", ["        var lines = File.ReadAllLines(path);"]);
        Assert.Single(rawLines);
        Assert.Contains("SharedDocumentReader", rawLines[0], StringComparison.Ordinal);

        var rawBytes = Offences("Fake/Bytes.cs", ["            bytes = File.ReadAllBytes(path);"]);
        Assert.Single(rawBytes);
        Assert.Contains("Fake/Bytes.cs:1", rawBytes[0], StringComparison.Ordinal);
        Assert.Contains("SharedDocumentReader", rawBytes[0], StringComparison.Ordinal);

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
        Assert.Empty(Offences("Fake/Bytes.cs", ["        var bytes = SharedDocumentReader.ReadAllBytes(path);"]));
        // A memory stream's ToArray is not a file read, and neither is anything else whose name
        // merely ends the same way - the rule is anchored on File. for that reason.
        Assert.Empty(Offences("Fake/Memory.cs", ["        var bytes = buffer.ReadAllBytes();"]));
    }

    /// <summary>
    /// A raw whole-file read: <c>File.ReadAllText</c>, <c>File.ReadAllLines</c> or
    /// <c>File.ReadAllBytes</c>.
    /// </summary>
    /// <remarks>
    /// <c>ReadAllBytes</c> was missing until task 6.1 and the omission is instructive: it is the
    /// only one of the three that reads a file without deciding an encoding, so it reads as a
    /// lower-level primitive rather than a convenience - but it opens at <c>FileShare.Read</c>
    /// exactly like its two siblings, which is the entire property this rule is about. The one
    /// site it missed was also the one file exempted from every rule, so the blind spot was
    /// invisible twice over and for two unrelated reasons.
    /// </remarks>
    [GeneratedRegex(@"\bFile\.ReadAll(Text|Lines|Bytes)\s*\(")]
    private static partial Regex RawReadExpression();

    /// <summary>A stream opened for reading; the sharing flags are checked separately.</summary>
    [GeneratedRegex(@"new FileStream\([^)]*FileAccess\.Read\b")]
    private static partial Regex ReadStreamExpression();
}
