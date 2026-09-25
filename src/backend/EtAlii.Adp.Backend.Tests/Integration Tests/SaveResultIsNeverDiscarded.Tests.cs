using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A store's save result is inspected rather than dropped (backend-centralization Requirement 3.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this guard can and cannot see, stated because the boundary is the point.</b> It sees a
/// result that is DISCARDED - a save standing alone as a statement, so nothing can have read it. It
/// does <b>not</b> see a MALFORMED result, and it never will: a null or unassigned result is covered
/// by <c>DocumentSaveResult</c> being a sealed record rather than a struct, so that asking such a
/// value whether it failed throws at the first access instead of answering. <b>Two guards with one
/// stated boundary between them beats one guard believed to cover both.</b>
/// </para>
/// <para>
/// <b>Why a source walk rather than an analyzer.</b> A <c>[MustUseReturnValue]</c>-style attribute
/// needs an analyzer package this tree does not carry, and the census found no analyzer here beyond
/// the SDK's and xUnit's. <c>ShapeOfFileAccessTests</c> already walks sources for exactly this class
/// of question, with an allow-list convention, and needs no new dependency.
/// </para>
/// <para>
/// <b>It lands GREEN with eight tracked entries, and that is deliberate.</b> Mindmap's
/// <c>Save</c> returns <c>void</c> today, so its eight statement-position calls are not discards
/// yet - they become discards the moment task 4 gives it a result, which is exactly when their
/// callers must convert. A guard sequenced after the conversion is a guard that gets cut when the
/// interesting work finishes; this one arrives first, carries the eight sites in
/// <see cref="Tracked"/> naming the task that deletes each line, and
/// <see cref="NoTrackedEntryOutlivesItsFix"/> refuses to let an entry linger once its site is clean.
/// </para>
/// <para>
/// <b>Named blind spots, so somebody else can re-run this rather than trust it.</b> A save reached
/// through a delegate, an interface variable named something this rule does not recognise, or
/// reflection is invisible. A result assigned to a variable that is then never read is NOT a discard
/// by this rule - a different defect, and one the compiler's own unused-value warnings reach. And
/// the rule is textual: it joins a statement's continuation lines, but a save buried inside a
/// conditional expression or a lambda body on one line may not be recognised as standing alone.
/// </para>
/// </remarks>
public partial class SaveResultIsNeverDiscardedTests
{
    private const string Rule = "a store's save result is discarded";
    private const string Replacement = "inspect it - `if (result.Failed)` - or pass it on";

    /// <summary>
    /// Receivers whose <c>Save</c> returns <c>void</c> BY DESIGN, so a statement-position call is
    /// correct rather than a discard. Never extend this to silence a real finding: a site that ought
    /// to inspect a result belongs in <see cref="Tracked"/> with a task, not here.
    /// </summary>
    private static readonly (string Receiver, string Reason)[] VoidByDesign =
    [
        ("AdpFileWriter", "the central writer's Save returns void and reports by throwing; it is the implementation a store's Save calls, not a store's Save"),
    ];

    /// <summary>
    /// Sites that will become discards when a conversion lands, each naming the task that deletes
    /// its line. A debt with an owner, kept visibly separate from <see cref="VoidByDesign"/>, which
    /// is a design decision with a reason: merging the two turns "not yet" into "never" silently.
    /// </summary>
    /// <summary>
    /// Sites that will become discards when a conversion lands, each naming the task that deletes
    /// its line. A debt with an owner, kept visibly separate from <see cref="VoidByDesign"/>, which
    /// is a design decision with a reason: merging the two turns "not yet" into "never" silently.
    /// </summary>
    /// <remarks>
    /// <b>Empty since task 4</b>, which converted mindmap's eight call sites - the seven entries here
    /// named that task and were deleted by it, which is the whole arrangement working rather than an
    /// accident. It is kept rather than removed because the next conversion group needs somewhere to
    /// put its debt, and because <see cref="NoTrackedEntryOutlivesItsFix"/> is what makes an entry
    /// expire rather than settle.
    /// </remarks>
    private static readonly (string File, string Reason)[] Tracked = [];

    /// <summary>A statement that BEGINS with a save call: nothing precedes it, so nothing read it.</summary>
    [GeneratedRegex(@"^(await\s+)?(?<receiver>[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*)\.Save\(")]
    private static partial Regex SaveStatement();

    /// <summary>A save whose result is used has something after the closing bracket, or before the call.</summary>
    [GeneratedRegex(@"\)\s*[.?]")]
    private static partial Regex ChainedAfterTheCall();

    /// <summary>
    /// Every discard in one file's lines. Pure, so the self-checks below can drive it with source
    /// that is written here rather than found in the tree - which is the only way to see this guard
    /// fail on demand.
    /// </summary>
    internal static IReadOnlyList<string> Offences(string relativePath, IReadOnlyList<string> lines)
    {
        var found = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var statement = lines[i].Trim();
            var match = SaveStatement().Match(statement);
            if (!match.Success)
            {
                continue;
            }

            // A statement can span lines; join until the brackets balance, so a multi-line save is
            // judged on the whole call rather than on its first line.
            var line = i;
            while (Unbalanced(statement) && line + 1 < lines.Count)
            {
                statement += " " + lines[++line].Trim();
            }

            var receiver = match.Groups["receiver"].Value;
            var tail = receiver.Split('.')[^1];
            if (VoidByDesign.Any(exempt => exempt.Receiver == tail))
            {
                continue;
            }

            // `store.Save(p).Warning` begins with the call and still reads the result.
            if (ChainedAfterTheCall().IsMatch(statement))
            {
                continue;
            }

            found.Add($"{relativePath}:{i + 1} - {Rule}. {Replacement}.");
        }

        return found;
    }

    private static bool Unbalanced(string statement) =>
        statement.Count(character => character == '(') != statement.Count(character => character == ')');

    [Fact]
    public void NoProductionCodeDiscardsAStoreSaveResult()
    {
        // Arrange, act.
        var offences = Survey()
            .Where(offence => !Tracked.Any(tracked => offence.RelativePath.Replace('\\', '/').EndsWith(tracked.File, StringComparison.Ordinal)))
            .Select(offence => offence.Offence)
            .ToList();

        // Assert.
        Assert.True(
            offences.Count == 0,
            $"A store's save result is dropped, so a failed save is reported to nobody:\n  {string.Join("\n  ", offences)}");
    }

    [Fact]
    public void TheWalkIsLooking()
    {
        // A guard that silently walks nothing passes for ever. This pins that it is reading the
        // tree at all, and the floor is deliberately far below the real count.
        Assert.True(ProductionSources().Count() > 200, "the walk found almost no production sources, so its silence means nothing");
    }

    [Fact]
    public void APlantedDiscardIsReported()
    {
        // THE SABOTAGE IS AT A CALL SITE, not in this file's own helper: a unit check over the
        // result type cannot see a caller that drops it, and the regression a later reader would
        // actually cause lives at the call site.
        var discarded = Offences("Fake/Command.cs", ["        _documents.Save(command.BodyPath, entry);"]);
        Assert.Single(discarded);
        Assert.Contains("discarded", discarded[0], StringComparison.Ordinal);

        var awaited = Offences("Fake/Async.cs", ["        await _documents.SaveAsync(path, entry);"]);
        Assert.Empty(awaited); // SaveAsync is not Save; recorded so the narrowness is deliberate rather than assumed

        var multiline = Offences("Fake/Multi.cs",
        [
            "        _documents.Save(",
            "            command.BodyPath,",
            "            entry);",
        ]);
        Assert.Single(multiline);
    }

    [Fact]
    public void APlantedUseIsNotReported()
    {
        // The other direction, and the half that stops this guard from reddening on correct code.
        // A guard that only ever sees its offence cannot tell a working rule from one that fires on
        // everything.
        Assert.Empty(Offences("Fake/Assigned.cs", ["        var result = _documents.Save(path, entry);"]));
        Assert.Empty(Offences("Fake/Condition.cs", ["        if (_documents.Save(path, entry).Failed)"]));
        Assert.Empty(Offences("Fake/Returned.cs", ["        return _documents.Save(path, entry);"]));
        Assert.Empty(Offences("Fake/Argument.cs", ["        Report(_documents.Save(path, entry));"]));
        Assert.Empty(Offences("Fake/Chained.cs", ["        _documents.Save(path, entry).Warning.Log();"]));
        Assert.Empty(Offences("Fake/Writer.cs", ["        AdpFileWriter.Save(path, text);"]));
    }

    [Fact]
    public void NoTrackedEntryOutlivesItsFix()
    {
        // A debt entry whose site is already clean is a finding: it would silence a real discard
        // introduced there later. Same rule the file-access guard enforces on its own tracked list.
        var offending = Survey().Select(offence => offence.RelativePath.Replace('\\', '/')).ToList();
        var stale = Tracked
            .Where(tracked => !offending.Any(path => path.EndsWith(tracked.File, StringComparison.Ordinal)))
            .Select(tracked => $"{tracked.File} ({tracked.Reason})")
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"These tracked entries no longer match a site, so delete them rather than leave them silencing a future discard:\n  {string.Join("\n  ", stale)}");
    }

    private static readonly Lazy<IReadOnlyList<(string RelativePath, string Offence)>> Surveyed =
        new(WalkTheTree, LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyList<(string RelativePath, string Offence)> Survey() => Surveyed.Value;

    private static IReadOnlyList<(string RelativePath, string Offence)> WalkTheTree()
    {
        var all = new List<(string, string)>();
        foreach (var file in ProductionSources())
        {
            var relative = IoPath.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');
            foreach (var offence in Offences(relative, File.ReadAllLines(file)))
            {
                all.Add((relative, offence));
            }
        }

        return all;
    }

    /// <summary>Production sources only: a test may discard a result it is not asserting on.</summary>
    private static IEnumerable<string> ProductionSources() =>
        new[] { "backend", "diagrams", "editors" }
            .Select(folder => IoPath.Combine(RepositoryRoot, "src", folder))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.EndsWith("Tests.cs", StringComparison.Ordinal))
            .Where(file => !file.Contains(".Tests", StringComparison.Ordinal));

    /// <summary>
    /// Located by walking up for the two folders that only the repository root has, the same way
    /// <c>ShapeOfFileAccessTests</c> does - rather than reaching into another test assembly's helper
    /// for it.
    /// </summary>
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

        throw new DirectoryNotFoundException($"No repository root above {AppContext.BaseDirectory}.");
    }
}
