using System.Diagnostics;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Routing;

/// <summary>Markers (FBL §12.2), candidates (FBL §12.3), readings (FBL §9.4) and globs (FBL §10.1).</summary>
public class RoutingTests
{
    private static readonly IReadOnlyList<FblBinding> _all = RealFileCorpus.AllBindings().Select(b => b.Binding).ToList();

    [Theory]
    [InlineData("<map version=\"freeplane 1.11.5\">\n<node TEXT=\"a\"/>\n</map>\n", true)]
    [InlineData("\n\n\n\n<map>\n</map>\n", true)]
    [InlineData("\n\n\n\n\n<map>\n</map>\n", false)]
    [InlineData("<mapping/>\n", false)]
    public void APatternMarkerLooksAtItsFirstLines(string body, bool matches)
    {
        // Arrange: the mind map's marker, ^<map[\s>] within 5 lines.
        var marker = RealFileCorpus.Binding("mindmap.fbl", "freeplane").Claims.Marker!;

        // Act and assert.
        Assert.Equal(matches, MarkerEvaluator.Matches(marker, Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("bundle:\n  name: x\n", true)]
    [InlineData("{ \"bundle\": { \"name\": \"x\" } }", true)]
    [InlineData("other: 1\n", false)]
    [InlineData("bundle: [unclosed\n", false)]
    public void ARootKeyMarkerReadsYamlAndJsonWithoutABinding(string body, bool matches)
    {
        // Arrange.
        var marker = new Marker("bundle", null, null, null, 0);

        // Act and assert.
        Assert.Equal(matches, MarkerEvaluator.Matches(marker, Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void AFirstLineMarkerIsMatchedAfterAByteOrderMark()
    {
        // Arrange.
        var marker = new Marker(null, null, "causal-loop", null, 0);
        var body = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("causal-loop 1\n"u8.ToArray()).ToArray();

        // Act and assert.
        Assert.True(MarkerEvaluator.Matches(marker, body));
        Assert.False(MarkerEvaluator.Matches(marker, "# causal-loop\n"u8.ToArray()));
    }

    [Fact]
    public void ARegistrationOnlyBindingIsNeverACandidate()
    {
        // Act: databricks.yml and Chart.yaml are claimed only through registrations.
        var job = Router.Candidates("nightly.yml", "resources:\n  jobs: {}\n"u8.ToArray(), _all);
        var chart = Router.Candidates("Chart.yaml", "apiVersion: v2\n"u8.ToArray(), _all);

        // Assert.
        Assert.Empty(job);
        Assert.Empty(chart);
    }

    [Fact]
    public void AnExtensionIsMatchedIgnoringCase()
    {
        // Act.
        var candidates = Router.Candidates("Plan.TML", "elements: []\n"u8.ToArray(), _all);

        // Assert.
        Assert.Equal(["timeline"], candidates.Select(c => c.Name));
    }

    [Fact]
    public void SeveralCandidatesAreAllReturned()
    {
        // Arrange: two bindings claiming the same extension.
        var a = new FblBinding { Name = "a", Claims = new Claims { Extensions = [".x"] }, Body = new BodySettings { Family = Family.Lines } };
        var b = new FblBinding { Name = "b", Claims = new Claims { Extensions = [".x"] }, Body = new BodySettings { Family = Family.Lines } };

        // Act.
        var candidates = Router.Candidates("file.x", [], [a, b]);

        // Assert: the router never chooses on the caller's behalf.
        Assert.Equal([a, b], candidates);
    }

    [Fact]
    public void AReadingWhoseSuggestMatchesIsOfferedFirst()
    {
        // Arrange.
        var turtle = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");
        var skos = "@prefix skos: <http://www.w3.org/2004/02/skos/core#> .\nex:s a skos:ConceptScheme .\n"u8.ToArray();

        // Act.
        var readings = Router.Readings(turtle, skos);

        // Assert.
        Assert.Equal(["w3c/skos", "w3c/rdf", "w3c/owl", "w3c/shacl"], readings);
        Assert.Equal("w3c/rdf", Router.Bare(turtle));
    }

    [Theory]
    [InlineData("templates/**", "templates/a/b.yaml", true)]
    [InlineData("templates/**", "values.yaml", false)]
    [InlineData("**/.helmignore", ".helmignore", true)]
    [InlineData("**/.helmignore", "charts/x/.helmignore", true)]
    [InlineData("charts/*/Chart.yaml", "charts/x/Chart.yaml", true)]
    [InlineData("charts/*/Chart.yaml", "charts/x/y/Chart.yaml", false)]
    [InlineData("values*.yaml", "values.prod.yaml", true)]
    [InlineData("file?.[ab]", "file1.a", true)]
    [InlineData("file?.[!ab]", "file1.a", false)]
    public void AGlobMatchesAsFblDefinesIt(string glob, string path, bool matches)
    {
        // Act and assert.
        Assert.Equal(matches, Glob.IsMatch(glob, path, ignoreCase: false));
    }

    [Fact]
    public void AFolderIsRecognisedAndItsFilesSelectedWithoutFollowingLinks()
    {
        // Arrange.
        var chart = RealFileCorpus.Binding("helm-chart.fbl", "chart");
        using var folder = new TemporaryFolder();
        folder.Write("Chart.yaml", "apiVersion: v2\n");
        folder.Write("values.yaml", "a: 1\n");
        folder.Write("templates/deployment.yaml", "kind: Deployment\n");
        folder.Write(".git/config", "x\n");
        folder.Write("README.md", "# chart\n");
        using var outside = new TemporaryFolder();
        outside.Write("secret.yaml", "x: 1\n");
        folder.Link("templates/linked", outside.Path);

        // Act.
        var recognised = FolderSubject.Recognise(chart, folder.Path);
        var files = FolderSubject.Files(chart, folder.Path).Select(f => f.RelativePath).ToList();

        // Assert: ordinal order, .git ignored, README unselected, the link not followed.
        Assert.True(recognised);
        Assert.Equal(["Chart.yaml", "templates/deployment.yaml", "values.yaml"], files);
        File.Delete(Path.Combine(folder.Path, "Chart.yaml"));
        Assert.False(FolderSubject.Recognise(chart, folder.Path));
    }
}

/// <summary>A folder under the system's temporary folder, deleted when disposed.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fbl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    private readonly List<string> _links = [];

    public string Path { get; }

    public string Write(string relative, string text)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary>
    /// Makes <paramref name="relative"/> a link to the folder <paramref name="target"/>. A symbolic
    /// link where the machine allows one; Windows refuses that without Developer Mode or elevation,
    /// and there a junction stands in - a reparse point all the same, which is what the code under
    /// test looks for, and one any account may create.
    /// </summary>
    public void Link(string relative, string target)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        try
        {
            Directory.CreateSymbolicLink(full, target);
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", full, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            mklink.WaitForExit();
        }

        Assert.True(new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint), $"'{full}' could not be made a link.");
        _links.Add(full);
    }

    public void Dispose()
    {
        // The links first and one at a time: a recursive delete takes a junction for a mounted
        // volume and is refused.
        foreach (var link in _links)
        {
            Directory.Delete(link);
        }

        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
