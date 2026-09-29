using EtAlii.Adp.Diagram.CausalLoopDiagram;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// <b>The shared restore edit, in the real host where every module registers one</b>
/// (backend-centralization R6.1, R6.2).
/// </summary>
/// <remarks>
/// The host registers a <see cref="RestoreDocumentCommand{TStore}"/> handler for each module on the
/// shared edit - causal-loop, databricks, rdf, functional-decomposition-graph and
/// gartner-hypecycle-graph at the time of writing - and the dispatcher finds a handler by the
/// command's type. So an undo and a redo dispatched here go through the same registrations a user's
/// do, and each module's restore must land in <b>that module's</b> store: the one whose cached
/// document the next read serves. A restore that reached another module's store would write the
/// file and leave this store serving the edited document, which is what the store assertions catch.
/// </remarks>
public sealed class SharedRestoreFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string CausalLoop =
        "causal-loop 1\r\n"
        + "\r\n"
        + "# a comment the restore must keep\r\n"
        + "variable population \"Population\"\r\n";

    private const string Turtle =
        "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "# a comment the restore must keep\r\n"
        + "ex:curie ex:discovered ex:radium .\r\n";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));

    public SharedRestoreFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        Directory.CreateDirectory(_root);
        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // The problem cache under this test's temp root, not the real user profile.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _root,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task ACausalLoopEdit_UndoesAndRedoes_ThroughItsOwnStore()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "feedback.cld");
        await File.WriteAllTextAsync(path, CausalLoop, TestContext.Current.CancellationToken);
        var store = _factory.Services.GetRequiredService<ICausalLoopDocumentStore>();

        // Act and assert.
        await AssertUndoAndRedo(
            path,
            CausalLoop,
            new AddVariableCommand(path, "births", "Births"),
            () => store.GetOrLoad(path).Document.Text);
    }

    [Fact]
    public async Task AnRdfEdit_UndoesAndRedoes_ThroughItsOwnStore()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "graph.ttl");
        await File.WriteAllTextAsync(path, Turtle, TestContext.Current.CancellationToken);
        var store = _factory.Services.GetRequiredService<IRdfDocumentStore>();

        // Act and assert.
        await AssertUndoAndRedo(
            path,
            Turtle,
            new AddRdfPrefixCommand(path, "foaf", "http://xmlns.com/foaf/0.1/"),
            () => store.GetOrLoad(path).Document.Text);
    }

    private async Task AssertUndoAndRedo(string path, string original, ICommand edit, Func<string> storeText)
    {
        var dispatcher = _factory.Services.GetRequiredService<ICommandDispatcher>();
        var cancellationToken = TestContext.Current.CancellationToken;

        // The store holds the document before the edit, as it does once a canvas has opened it.
        Assert.Equal(original, storeText());

        var edited = await dispatcher.DispatchAsync(edit, cancellationToken);
        Assert.True(edited.IsSuccess, edited.Error);
        var afterEdit = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.NotEqual(original, afterEdit);
        Assert.NotNull(edited.Inverse);

        // Undo: the file comes back byte for byte, and this module's store serves it.
        var undone = await dispatcher.DispatchAsync(edited.Inverse, cancellationToken);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(original, await File.ReadAllTextAsync(path, cancellationToken));
        Assert.Equal(original, storeText());
        Assert.Same(edit, undone.Inverse);

        // Redo: the inverse of the restore is the original edit, which runs again.
        var redone = await dispatcher.DispatchAsync(undone.Inverse!, cancellationToken);
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.Equal(afterEdit, await File.ReadAllTextAsync(path, cancellationToken));
        Assert.Equal(afterEdit, storeText());
    }
}
