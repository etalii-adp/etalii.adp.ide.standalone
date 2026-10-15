using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// A file of a folder subject the host cannot open (FBL §10.3): it is DISL's <c>std.unparseable</c> located
/// at that file, with the reason, and the rest of the folder is read. A file another process holds without
/// sharing stands in for every way a selected file fails to open, since it is the one a test can hold
/// open on every platform.
/// </summary>
public class FolderBodyUnopenedFileTests
{
    private const string PluginId = "net.example.folder";
    private const int Settle = 250;

    /// <summary>A folder binding of no tool type, as in <see cref="FolderBodyTests"/>.</summary>
    private static FblBinding Binding()
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""
            { "fbl": "0.1", "bindings": { "notes": {
              "claims": { "names": ["index.txt"] },
              "body": {
                "kind": "folder",
                "recognise": { "all": ["index.txt"] },
                "files": [
                  { "name": "index", "glob": "index.txt", "family": "lines" },
                  { "name": "pages", "glob": "pages/**", "family": "lines" }
                ],
                "settle": {{Settle}}
              },
              "reader": { "plugin": "{{PluginId}}", "version": "^0.1.0" }
            } } }
            """), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        return document!.Bindings["notes"];
    }

    private static TemporaryFolder Notes()
    {
        var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        folder.Write("pages/a.txt", "a");
        folder.Write("pages/b.txt", "b");
        folder.Write("pages/c.txt", "c");
        return folder;
    }

    /// <summary>Holds the file at <paramref name="path"/> open, sharing nothing, so no other reader can open it.</summary>
    private static FileStream Held(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public void AFileThatCannotBeOpened_IsAFindingAtThatFile_AndTheRestIsRead()
    {
        // Arrange.
        using var folder = Notes();
        var plugin = new RecordingFolderPlugin(PluginId);
        var held = Path.Combine(folder.Path, "pages", "b.txt");

        // Act.
        FblModel model;
        IReadOnlyList<string> dependencies;
        using (Held(held))
        {
            using var body = FolderBody.Open(folder.Path, Binding(), plugin);
            model = body.Model;
            dependencies = body.Dependencies;
        }

        // Assert: the plugin is handed the others, the file is a finding at itself, and it is still watched.
        Assert.Equal(["index.txt", "pages/a.txt", "pages/c.txt"], Assert.Single(plugin.Reads).Files.Select(f => f.RelativePath));
        Assert.False(model.Unreadable);
        var finding = Assert.Single(model.Findings);
        Assert.Equal((FindingCodes.Unparseable, FindingSeverity.Error, "pages/b.txt"), (finding.Code, finding.Severity, finding.Location?.File));
        Assert.Equal("The file could not be opened, so it is not read.", finding.Message);
        Assert.Contains(held, dependencies);
    }

    [Fact]
    public void AFileThatCanBeOpenedAgain_IsReadOnceItsChangeSettles()
    {
        // Arrange.
        using var folder = Notes();
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);
        var held = Path.Combine(folder.Path, "pages", "b.txt");
        FolderBody body;
        using (Held(held))
        {
            body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);
        }

        // Act.
        using (body)
        {
            body.Notify(held);
            time.Advance(TimeSpan.FromMilliseconds(Settle));

            // Assert.
            Assert.Equal(2, plugin.Reads.Count);
            Assert.Equal(["index.txt", "pages/a.txt", "pages/b.txt", "pages/c.txt"], plugin.Reads[1].Files.Select(f => f.RelativePath));
            Assert.Empty(body.Model.Findings);
        }
    }
}
