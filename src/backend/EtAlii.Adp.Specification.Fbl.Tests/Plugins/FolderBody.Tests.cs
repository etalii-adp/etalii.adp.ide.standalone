using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// The folder body (FBL §10, §11.2, §16): a folder subject recognised, its files selected and handed
/// to the binding's plugin in one read, each over a folder on disk with a recording plugin, since no
/// plugin is implemented by this library.
/// </summary>
public class FolderBodyTests
{
    private const string PluginId = "net.example.folder";

    /// <summary>A folder binding of no tool type: <c>index.txt</c> recognises it, and it reads that file and everything under <c>pages/</c>.</summary>
    private static FblBinding Binding(string? readOnly = null)
    {
        var readOnlyKey = readOnly is null ? "" : $$""", "readOnly": "{{readOnly}}" """;
        var json = $$"""
            { "fbl": "0.1", "bindings": { "notes": {
              "claims": { "names": ["index.txt"] },
              "body": {
                "kind": "folder",
                "recognise": { "all": ["index.txt"] },
                "files": [
                  { "name": "index", "glob": "index.txt", "family": "lines" },
                  { "name": "pages", "glob": "pages/**", "family": "lines" }
                ],
                "ignore": ["pages/**/.hidden"]
              },
              "reader": { "plugin": "{{PluginId}}", "version": "^0.1.0" }{{readOnlyKey}}
            } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        return document!.Bindings["notes"];
    }

    [Fact]
    public void EverySelectedFileIsHandedToThePluginInOneReadInOrdinalOrder()
    {
        // Arrange: written out of order, with an ignored and an unselected file.
        using var folder = new TemporaryFolder();
        folder.Write("pages/b/c.txt", "c");
        folder.Write("pages/a.txt", "a");
        folder.Write("index.txt", "index");
        folder.Write("pages/Z.txt", "Z");
        folder.Write("pages/b/.hidden", "never");
        folder.Write("notes.md", "never");
        var plugin = new RecordingFolderPlugin(PluginId);

        // Act.
        var body = FolderBody.Open(folder.Path, Binding(), plugin);

        // Assert.
        var request = Assert.Single(plugin.Reads);
        Assert.Equal(["index.txt", "pages/Z.txt", "pages/a.txt", "pages/b/c.txt"], request.Files.Select(f => f.RelativePath));
        Assert.Equal(["index", "Z", "a", "c"], request.Files.Select(f => Encoding.UTF8.GetString(f.Bytes)));
        Assert.Equal(4, body.Model.Elements.Count);
        Assert.False(body.Model.Unreadable);
    }

    [Fact]
    public void AReparsePointIsNeverFollowed()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        folder.Write("pages/a.txt", "a");
        using var outside = new TemporaryFolder();
        outside.Write("secret.txt", "outside");
        folder.Link("pages/linked", outside.Path);
        var plugin = new RecordingFolderPlugin(PluginId);

        // Act.
        FolderBody.Open(folder.Path, Binding(), plugin);

        // Assert.
        Assert.Equal(["index.txt", "pages/a.txt"], Assert.Single(plugin.Reads).Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void AFolderThatFailsRecogniseOrIsGoneIsUnreadableAndThePluginIsNotAsked()
    {
        // Arrange: no index.txt.
        using var folder = new TemporaryFolder();
        folder.Write("pages/a.txt", "a");
        var plugin = new RecordingFolderPlugin(PluginId);

        // Act.
        var body = FolderBody.Open(folder.Path, Binding(), plugin);

        // Assert.
        Assert.True(body.Model.Unreadable);
        Assert.True(body.IsReadOnly);
        Assert.Empty(body.Model.Elements);
        Assert.Equal(FindingCodes.Unparseable, Assert.Single(body.Model.Findings).Code);
        Assert.Empty(plugin.Reads);

        // A folder recognised once and gone since is unreadable when read again.
        folder.Write("index.txt", "index");
        body.Reread();
        Assert.False(body.Model.Unreadable);
        Directory.Delete(folder.Path, recursive: true);
        body.Reread();
        Assert.True(body.Model.Unreadable);
        Assert.Contains("no longer exists", Assert.Single(body.Model.Findings).Message, StringComparison.Ordinal);
        Assert.Single(plugin.Reads);
    }

    [Fact]
    public void AMissingPluginOpensTheFolderReadOnlyWithAFinding()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        var other = new RecordingFolderPlugin("net.example.other");

        // Act.
        var body = FolderBody.Open(folder.Path, Binding(), other);

        // Assert.
        Assert.True(body.IsReadOnly);
        Assert.False(body.Model.Unreadable);
        var finding = Assert.Single(body.Model.Findings);
        Assert.Equal(FindingCodes.PluginMissing, finding.Code);
        Assert.Contains(PluginId, finding.Message, StringComparison.Ordinal);
        Assert.Empty(other.Reads);
        Assert.IsType<PlanResult.Refused>(body.Plan(new ModelChange.Remove("index.txt")));
    }

    [Fact]
    public void AFindingOfAFolderSubjectHasAFileAndALine()
    {
        // Arrange: the plugin reports three findings about pages/a.txt, two of them without a file.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        folder.Write("pages/a.txt", "a");
        var subject = new Dictionary<string, string> { [FolderBody.SubjectDetail] = "pages/a.txt" };
        var plugin = new RecordingFolderPlugin(PluginId)
        {
            Findings =
            [
                new Finding("x.no-location", FindingSeverity.Warning, "No location.", null) { Detail = subject },
                new Finding("x.no-file", FindingSeverity.Warning, "A line, no file.", new SourceLocation("", 7, 2, 3)) { Detail = subject },
                new Finding("x.own-file", FindingSeverity.Warning, "Its own file.", new SourceLocation("index.txt", 1, 1, 0)) { Detail = subject },
            ],
        };

        // Act.
        var body = FolderBody.Open(folder.Path, Binding(), plugin);

        // Assert: every element carries its file, and every finding has a file and a line.
        Assert.Equal(["index.txt", "pages/a.txt"], body.Model.Elements.Select(e => e.File));
        Assert.Equal(
            [new SourceLocation("pages/a.txt", 3, 1, 0), new SourceLocation("pages/a.txt", 7, 2, 3), new SourceLocation("index.txt", 1, 1, 0)],
            body.Model.Findings.Select(f => f.Location));
    }

    [Fact]
    public void AFileOverTheSizeLimitIsHandedByItsPathWithNoBytes()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        folder.Write("pages/large.txt", new string('x', 11));
        folder.Write("pages/small.txt", new string('x', 10));
        var plugin = new RecordingFolderPlugin(PluginId);

        // Act.
        FolderBody.Open(folder.Path, Binding(), plugin, new FblOptions { MaxBodyBytes = 10 });

        // Assert.
        var files = Assert.Single(plugin.Reads).Files;
        Assert.Equal(["index.txt", "pages/large.txt", "pages/small.txt"], files.Select(f => f.RelativePath));
        Assert.Equal([false, true, false], files.Select(f => f.IsOversized));
        Assert.Equal([5, 0, 10], files.Select(f => f.Bytes.Length));
    }

    [Fact]
    public void ASupplementaryEntryIsHandedWithNoBytesBesideTheFiles()
    {
        // Arrange: an empty folder, which no file rule can select.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        Directory.CreateDirectory(Path.Combine(folder.Path, "pages", "empty"));
        var plugin = new RecordingFolderPlugin(PluginId);
        var options = new FblOptions
        {
            SupplementaryEntries = root => Directory.GetDirectories(Path.Combine(root, "pages")).Select(d => "pages/" + Path.GetFileName(d) + "/").ToList(),
        };

        // Act.
        FolderBody.Open(folder.Path, Binding(), plugin, options);

        // Assert.
        var request = Assert.Single(plugin.Reads);
        Assert.Equal(["pages/empty/"], request.SupplementaryEntries);
        Assert.Equal(["index.txt"], request.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void AReadOnlyFolderRefusesEveryChangeWithTheBindingsReasonAndNeverPlans()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        var plugin = new RecordingFolderPlugin(PluginId);
        var body = FolderBody.Open(folder.Path, Binding(readOnly: "Change it in its files."), plugin);
        ModelChange[] changes =
        [
            new ModelChange.Set("index.txt", new Dictionary<string, object?> { ["text"] = "x" }),
            new ModelChange.Remove("index.txt"),
            new ModelChange.Add("Page", null, new Dictionary<string, object?>()),
        ];

        // Act.
        var results = changes.Select(body.Plan).ToList();

        // Assert.
        Assert.True(body.IsReadOnly);
        Assert.All(results, result => Assert.Equal("Change it in its files.", Assert.IsType<PlanResult.Refused>(result).Reason));
        Assert.Equal(0, plugin.Plans);

        // The same folder under a writable binding does ask the plugin, so the count above can fail.
        var writable = FolderBody.Open(folder.Path, Binding(), plugin);
        Assert.Equal("The recording plugin plans nothing.", Assert.IsType<PlanResult.Refused>(writable.Plan(changes[1])).Reason);
        Assert.Equal(1, plugin.Plans);
    }
}

/// <summary>
/// A recording folder plugin: every file it is handed is one element, its id the file's path and
/// its <see cref="FblElement.File"/> the same, at line 3; it records every request and plans nothing.
/// </summary>
internal sealed class RecordingFolderPlugin(string id) : IPersistencePlugin
{
    public string Id { get; } = id;

    public List<PluginReadRequest> Reads { get; } = [];

    public int Plans { get; private set; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    public IReadOnlyList<string> Watched { get; init; } = [];

    public PluginReadResult Read(PluginReadRequest request)
    {
        Reads.Add(request);
        var elements = request.Files.Select(file => new FblElement(file.RelativePath, true, "Page", "page", false,
            new Dictionary<string, object?> { ["text"] = Encoding.UTF8.GetString(file.Bytes) }, null, null, null, null, new Span(0, file.Bytes.Length), 3)
        {
            File = file.RelativePath,
        }).ToList();
        return new PluginReadResult(elements, Findings, false);
    }

    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        Plans++;
        return new PluginPlanResult.Refused("The recording plugin plans nothing.");
    }

    public byte[] Template(PluginTemplateRequest request) => throw new NotSupportedException("A folder is never created here.");

    public IReadOnlyList<string> Watch(PluginReadResult last) => Watched;
}
