using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The module's shipped examples, opened by the module's own code, so they cannot drift from
/// what it supports.
/// </summary>
/// <remarks>
/// The walk is live, so a file added to <c>examples/</c> is covered on arrival - and a file
/// nothing opens cannot exist here. Requirement 4.1 asks for exactly this: the shipped example
/// is opened by the module's own tests rather than only by a person looking at it.
/// </remarks>
public class ExamplesTests
{
    /// <summary>
    /// The module's <c>examples/</c> folder, found by walking up from the test binary rather
    /// than by counting <c>..</c> segments - the count changes with the build layout, the
    /// folder name does not.
    /// </summary>
    private static string ExamplesRoot { get; } = Locate("src", "diagrams", "dependency-graph", "examples");

    /// <summary>The combined project's replica of this module's examples (structure.md's replication rule).</summary>
    private static string ReplicaRoot { get; } = Locate("src", "examples", "diagrams", "dependency-graph");

    private static string Locate(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine([directory.FullName, .. segments]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"The {string.Join('/', segments)} folder was not found above the test binary.");
    }

    public static TheoryData<string> EveryExampleFile()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ExamplesRoot, "*", SearchOption.AllDirectories))
        {
            data.Add(IoPath.GetRelativePath(ExamplesRoot, file));
        }

        return data;
    }

    public static TheoryData<string> EveryExampleGraph()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ExamplesRoot, "*.dgr", SearchOption.AllDirectories))
        {
            data.Add(IoPath.GetRelativePath(ExamplesRoot, file));
        }

        return data;
    }

    [Fact]
    public void TheWalk_FindsTheTrackedExampleSet()
    {
        // Arrange and act.
        // Two files at the time of writing - one registration and one body. The count moving
        // down unexpectedly is what this guards: a vanished example silently shrinks the
        // theories below rather than failing them.
        Assert.True(EveryExampleFile().Count() >= 2, "the examples walk lost files");
        Assert.NotEmpty(EveryExampleGraph());
    }

    [Theory]
    [MemberData(nameof(EveryExampleGraph))]
    public void EveryExample_ParsesAndDeclaresSomething(string relativePath)
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(ExamplesRoot, relativePath));

        // Act.
        var model = DependencyGraphParser.Parse(LineDocument.Parse(text));

        // Assert.
        // An example that parses to nothing would show an empty canvas to the first person who
        // opened it, which is worse than shipping no example at all.
        Assert.NotEmpty(model.Elements);
        Assert.NotEmpty(model.Relations);
    }

    [Theory]
    [MemberData(nameof(EveryExampleGraph))]
    public void EveryExample_RoundTripsByteForByte(string relativePath)
    {
        // Arrange.
        var original = File.ReadAllText(IoPath.Combine(ExamplesRoot, relativePath));

        // Act.
        var document = LineDocument.Parse(original);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Theory]
    [MemberData(nameof(EveryExampleGraph))]
    public void EveryExample_IsFreeOfProblems(string relativePath)
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(ExamplesRoot, relativePath));
        var model = DependencyGraphParser.Parse(LineDocument.Parse(text));

        // Act.
        var problems = DependencyGraphRuleSet.Judge(model);

        // Assert.
        // Shipped material that lights up the problems panel teaches the first-time user to
        // ignore it.
        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(EveryExampleGraph))]
    public void EveryExample_CarriesNoDate(string relativePath)
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(ExamplesRoot, relativePath));

        // Assert.
        // The deletion, held by the shipped material itself: an example forked from the
        // timeline's roadmap and not finished would show its dates here first.
        foreach (var key in new[] { "begin:", "end:", "duration:" })
        {
            Assert.DoesNotContain(key, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", text);
    }

    [Theory]
    [MemberData(nameof(EveryExampleGraph))]
    public void EveryExample_UsesRealNamesRatherThanPlaceholders(string relativePath)
    {
        // Arrange.
        // The no-placeholder rule, asserted rather than trusted. "New element" and "New node" are
        // what a gesture names a node before anybody renames it, so an example carrying one is an
        // example somebody built on the canvas and shipped without finishing.
        var model = DependencyGraphParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(ExamplesRoot, relativePath))));

        // Assert.
        Assert.All(model.Elements, element =>
        {
            if (element == null!)
            {
                throw new ArgumentNullException(nameof(element));
            }

            Assert.NotEmpty(element.Label);
            Assert.DoesNotContain("New element", element.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("New node", element.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lorem", element.Label, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public void EveryRegistration_NamesThisType(string relativePath)
    {
        // Arrange.
        if (!relativePath.EndsWith(".adp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Act.
        var mime = File.ReadLines(IoPath.Combine(ExamplesRoot, relativePath)).First().Trim();

        // Assert.
        // The example-registration walk in EtAlii.Adp.Backend.Tests checks this against the
        // deployed catalog; this checks it without a host, so a wrong MIME line fails in the
        // module that owns it too.
        Assert.Equal("generic/dependencies", mime);
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public void EveryExample_HasAByteIdenticalReplicaInTheCombinedProject(string relativePath)
    {
        // Arrange.
        // structure.md's rule - a module's examples and the combined project's copy must never
        // drift. Byte-identical is achievable here because this type's registration carries no
        // project-relative `body:` header to rewrite: the body is the `.adp`'s own sibling.
        var moduleCopy = IoPath.Combine(ExamplesRoot, relativePath);
        var replica = IoPath.Combine(ReplicaRoot, relativePath);

        // Act and assert.
        Assert.True(File.Exists(replica), $"{relativePath} has no replica under src/examples/diagrams/dependency-graph");
        Assert.Equal(File.ReadAllBytes(moduleCopy), File.ReadAllBytes(replica));
    }
}
