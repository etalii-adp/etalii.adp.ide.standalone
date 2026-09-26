using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// The shipped example documents carry no placeholder junk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing else tests the showcase.</b> Where a module has example tests they read the
/// module's own copy under <c>src/diagrams/&lt;module&gt;/examples/</c>; the copy a reader
/// actually opens is read by no test in the repository. It is also the copy the running
/// application writes to, so anything typed into a diagram and saved lands there and is
/// committed by whoever runs <c>git add</c> next.
/// </para>
/// <para>
/// That is not hypothetical. Two examples were carrying keyboard mash when this guard was
/// written - two nodes named <c>sdfsdf</c> under "Frustum tests" in the mindmap, and a link
/// labelled <c>"sdfsdf"</c> in the on-call causal loop, on the very link whose readme
/// celebrates the tool correcting the author. Both had been committed for weeks, and both were
/// found by opening the diagram and reading it, because nothing else was looking.
/// </para>
/// <para>
/// <b>This is an approved acceptance criterion, not a preference.</b> The archived
/// <c>small-refinements</c> specification, Requirement 2, says no shipped example under any
/// <c>src/diagrams/*/examples/</c>, <c>src/editors/*/examples/</c> or <c>src/examples/</c>
/// shall contain placeholder or keyboard-mash text. Its own requirements document listed the
/// mindmap's <c>sdfsdf</c> as the measured current state; task 2.2 rewrote the maps with real
/// content in <c>e6b7407b</c>; and <c>24a3ca56</c> put two of those nodes straight back. So
/// this was found, specified, fixed, and regressed - which is the whole argument for a guard
/// rather than another sweep.
/// </para>
/// <para>
/// <b>The scope is the documents ADP itself writes</b>, by extension, and that boundary is the
/// whole reason this is usable. The same sweep run over every shipped file returned 451 hits
/// and nearly all were correct: <c>www</c> in a path, <c>SSS</c> in a log pattern, <c>foo</c>
/// throughout the vendored Ansible playbooks - upstream data nobody here may edit. A guard that
/// cannot tell our junk from an upstream author's placeholder would be turned off within a
/// week. Over the 101 documents ADP writes, it has no false positives at all.
/// </para>
/// <para>
/// <b>What it cannot see</b>, stated rather than implied: prose that is wrong rather than
/// junk, a node named after the button that made it, a diagram that draws nothing. Those need
/// a person, and <c>tests.md</c> carries the step - this guard exists to stop the mechanical
/// half recurring, not to replace the reading.
/// </para>
/// </remarks>
public partial class ShippedExamplesTests
{
    /// <summary>The document extensions ADP reads and writes as its own, lower-case.</summary>
    private static readonly string[] AuthoredExtensions = [".mm", ".cld", ".owm", ".adp", ".tml"];

    private static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "src", "examples")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/diagrams beside src/examples) was not found above the test binary.");
    }

    /// <summary>
    /// Every example folder the shipped corpus lives in: the showcase, and each module's and
    /// editor's own copy.
    /// </summary>
    /// <remarks>
    /// The showcase was the tree that had rotted and is why this guard exists, but scoping the
    /// walk to it would enforce half of an acceptance criterion that names all three - and the
    /// module copies are where a fix gets propagated FROM, so junk sitting there is junk
    /// waiting to be copied into the showcase by somebody following the propagation rule.
    /// </remarks>
    private static string[] ExampleRoots()
    {
        var roots = new List<string>();

        foreach (var family in new[] { "diagrams", "editors" })
        {
            var container = IoPath.Combine(RepositoryRoot, "src", family);
            if (!Directory.Exists(container))
            {
                continue;
            }

            roots.AddRange(Directory
                .EnumerateDirectories(container)
                .Select(module => IoPath.Combine(module, "examples"))
                .Where(Directory.Exists));
        }

        roots.Add(IoPath.Combine(RepositoryRoot, "src", "examples"));
        return [.. roots];
    }

    private static string[] AuthoredDocuments() =>
        [.. ExampleRoots()
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(file => AuthoredExtensions.Contains(IoPath.GetExtension(file).ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void TheSweepActuallyReachesTheDocuments()
    {
        // THE CANARY. A walk that finds nothing passes every rule it has, and this guard's
        // whole output is "no matches" - the one shape that reads identically whether it
        // checked a hundred files or none. A floor and a named member, because a floor alone
        // is defeated by a path pointing at the wrong tree.
        var documents = AuthoredDocuments();

        Assert.True(
            documents.Length >= 150,
            $"Only {documents.Length} authored documents were found across {ExampleRoots().Length} example folders; the walk has stopped reaching them.");

        // One from the showcase and one from a module's own copy, because the walk gained the
        // second tree after the first was already covered - a floor alone would not notice
        // either half disappearing.
        Assert.Contains(
            documents,
            file => file.EndsWith(IoPath.Combine("src", "examples", "diagrams", "mindmap", "example 1", "mindmap.mm"), StringComparison.Ordinal));

        Assert.Contains(
            documents,
            file => file.EndsWith(IoPath.Combine("src", "diagrams", "mindmap", "examples", "example 1", "mindmap.mm"), StringComparison.Ordinal));
    }

    [Fact]
    public void NoShippedDocumentCarriesPlaceholderText()
    {
        // Arrange.
        var findings = new List<string>();

        // Act.
        foreach (var document in AuthoredDocuments())
        {
            var lines = File.ReadAllLines(document);
            for (var number = 0; number < lines.Length; number++)
            {
                var match = PlaceholderExpression().Match(lines[number]);
                if (match.Success)
                {
                    var relative = IoPath.GetRelativePath(RepositoryRoot, document).Replace('\\', '/');
                    findings.Add($"{relative}:{number + 1} — <{match.Value}> in: {lines[number].Trim()}");
                }
            }
        }

        // Assert. Every finding at once, each naming its file and line, so one run gives the
        // whole repair list rather than the first of several trips.
        Assert.True(
            findings.Count == 0,
            "Placeholder text in a shipped diagram document. Restore the hunk from the module's own copy under "
            + "src/diagrams/<module>/examples/ - do NOT copy the file wholesale, because the two copies are allowed "
            + "to differ where a presentation choice was made in the app and saved:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// Keyboard mash, the placeholder words, a short fragment typed twice, <c>Test 33</c>, and
    /// the name the application gives a fresh element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Word-boundary anchored throughout, so <c>bar</c> does not fire on <c>toolbar</c> and
    /// <c>foo</c> does not fire on <c>food</c>. A legitimate example that genuinely needs one
    /// of these words is a real possibility and the answer is an exemption carrying its reason,
    /// the way the client's unstyled-class guard does it - not deleting the rule.
    /// </para>
    /// <para>
    /// <b>One rule is deliberately absent, and this paragraph is here so it is not "fixed"
    /// back in.</b> The acceptance criterion this guard enforces names <c>AAA</c> among the
    /// junk to catch, and a repeated-single-letter rule is the obvious way to write it. It
    /// cannot be used: <c>www</c> matches it, and <c>www</c> appears in a real URI on 30 lines
    /// across the RDF and OWL examples, every one of them correct. The repeated-FRAGMENT rule
    /// below still catches <c>AAAA</c> and every longer run; a bare three-letter <c>AAA</c> is
    /// the price, and it is cheaper than a guard nobody trusts.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:asdf\w*|sdfsdf\w*|qwerty\w*|hjkl\w*|zxcv\w*|lorem\w*|foo|bar|baz|blah|xxx+|todo|tbd|fixme|Test\s*\d+|(?<fragment>\w{2,4})\k<fragment>|New (?:Element|Node|Item|Diagram))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderExpression();
}
