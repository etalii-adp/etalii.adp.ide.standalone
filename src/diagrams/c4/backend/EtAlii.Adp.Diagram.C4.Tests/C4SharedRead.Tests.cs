
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// This module's two file reads against the world's write side: a document load and a
/// registration's <c>view:</c> header must both be readable while a writer holds the file,
/// per the sharing discipline SharedDocumentReader carries. Windows enforces sharing, so
/// these guards bite there.
/// </summary>
public class C4SharedReadTests : IDisposable
{
    private readonly string _root;
    private readonly C4DocumentStore _documents = new();
    private readonly C4ElementMapper _mapper = new(C4Metrics.Default, new C4LayoutSidecar());
    private readonly IHistoryStackStore _historyStacks = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider().GetRequiredService<IHistoryStackStore>();

    public C4SharedReadTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private const string Model = """
        workspace "Bank" {
            model {
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves." "React"
                }
            }
            views {
                systemContext s "context" {
                    include *
                }
                container s "containers" {
                    include *
                }
            }
        }
        """;

    [Fact]
    public void GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        var path = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(path, Model);
        using var editor = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var document = _documents.GetOrLoad(path);

        // Assert: the model itself, not the empty fallback.
        Assert.Equal(Model, document.ToText());
    }

    [Fact]
    public async Task Open_ReadsARegistrationAnEditorIsStillWriting_AndStillNamesItsView()
    {
        // Arrange.
        // RenameEntryCommandHandler rewrites a registration's body: header in place, so a
        // session open and that write can genuinely overlap. A refused header read would fall
        // back to the document's first view - silently the wrong diagram.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var adp = IoPath.Combine(_root, "containers.adp");
        await File.WriteAllTextAsync(adp, "c4/container\nbody: model.dsl\nview: containers\n", TestContext.Current.CancellationToken);
        using var editor = new FileStream(adp, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        await using var session = (C4Session)new C4SessionFactory(new DiagramOrigin("c4", "container"), _documents, _mapper, _historyStacks)
            .Open(ShortGuid.NewShortGuid(), _root, body, adp);

        // Assert.
        Assert.Equal("containers", session.View()!.Key);
    }
}
