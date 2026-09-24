using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// No <see cref="FileSystemWatcher"/> in this tree is enabled before its handlers are attached.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the hole is.</b> <c>EnableRaisingEvents = true</c> set inside the object initializer
/// makes the watcher live at the end of that statement, while the <c>+=</c> lines that give it
/// somewhere to raise come after. Anything raised in between is not dropped by the OS and not
/// refused by anything - it is received by nobody, because there is nobody. Setting it as the
/// last statement instead removes the window rather than narrowing it.
/// </para>
/// <para>
/// <b>Why a source guard rather than a behavioural one.</b> The window is a thread-scheduling gap
/// between two statements, and no test can provoke it deterministically: a test that writes the
/// file must first wait for the object that watches it, by which time the gap has closed. That was
/// measured - holding the gap open for three seconds on purpose lost no notification at all,
/// because nothing writes during it. So there is nothing for an assertion to observe, and the only
/// check that can hold this is one over the wiring itself.
/// </para>
/// <para>
/// <b>Seen to fail:</b> against the three components that had it the other way round -
/// <c>PlainEditorSession</c>, <c>MarkdownEditorSession</c> and <c>SolutionWatcher</c> - this named
/// all three and no others. Four already did it correctly: <c>RootFolderWatcher</c>,
/// <c>TrackedProblemRoot</c>, <c>AnsibleWatchedFolder</c> and <c>HelmWatchedFolder</c>.
/// </para>
/// </remarks>
public class WatcherWiringTests
{
    /// <summary>
    /// <c>EnableRaisingEvents</c> appearing anywhere between a <c>new FileSystemWatcher</c> and the
    /// closing brace of its object initializer. Matching the initializer rather than the assignment
    /// is what makes the difference detectable: the same text as a later statement is correct.
    /// <para>
    /// <b>The first version of this pattern matched nothing and passed against all three
    /// offenders.</b> It bounded the constructor arguments with <c>\([^)]*\)</c>, and
    /// <c>Path.GetFileName(path)</c> puts a <c>)</c> inside them, so the pattern never reached the
    /// brace. Only the deliberate revert showed it: <b>a guard that cannot fail looks exactly like
    /// a tree that is clean.</b> Hence the floor test below.
    /// </para>
    /// </summary>
    private static readonly Regex EnabledInInitializer = new(
        @"new\s+FileSystemWatcher[^;{]*\{[^;}]*EnableRaisingEvents",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string RepositoryRoot { get; } = Locate();

    [Fact]
    public void NoWatcherIsEnabledInsideItsObjectInitializer()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("FileSystemWatcher", StringComparison.Ordinal))
            {
                continue;
            }

            if (EnabledInInitializer.IsMatch(text))
            {
                offenders.Add(IoPath.GetRelativePath(RepositoryRoot, file).Replace('\\', '/'));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A FileSystemWatcher is enabled inside its object initializer, so it is live before its "
                + "handlers are attached. Set EnableRaisingEvents as the LAST statement instead: "
                + string.Join(", ", offenders));
    }

    /// <summary>
    /// The guard's own floor. A pattern that matches nothing and a tree that is clean print the
    /// same green, so this asserts the sweep actually reached the watchers - without it, a typo in
    /// the glob would read as compliance.
    /// </summary>
    [Fact]
    public void TheSweepReachesEveryWatcherInTheTree()
    {
        var withWatchers = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("new FileSystemWatcher", StringComparison.Ordinal))
            .Select(file => IoPath.GetFileNameWithoutExtension(file))
            .ToList();

        // The four that were already correct, by name: if the sweep stops seeing these it is no
        // longer looking where the hole was found.
        Assert.Contains("RootFolderWatcher", withWatchers);
        Assert.Contains("TrackedProblemRoot", withWatchers);
        Assert.Contains("PlainEditorSession", withWatchers);
        Assert.Contains("SolutionWatcher", withWatchers);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory
            .EnumerateFiles(IoPath.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test assembly.");
    }
}
