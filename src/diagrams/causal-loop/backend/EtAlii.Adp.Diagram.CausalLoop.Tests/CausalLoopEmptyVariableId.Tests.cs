using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// An empty variable id must never reach the file as half a link.
/// </summary>
/// <remarks>
/// The chain Architect 1 measured: <c>variable ""</c> parsed as a declared variable, a connect
/// raised as <c>rel:a-&gt;</c> read as a link to it, the writer emitted <c>link a -&gt;  +</c>, and
/// the re-read silently named a variable "+" with an unstated polarity - reporting no problem at
/// all. Each half is refused on its own, so neither depends on the other holding.
/// </remarks>
public class CausalLoopEmptyVariableIdTests : IDisposable
{
    private const string WithEmptyVariable =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable \"\" \"Nameless\"\r\n";

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;
    private readonly CausalLoopDocumentStore _store = new();
    private readonly CausalLoopContextActionProvider _actions;

    public CausalLoopEmptyVariableIdTests()
    {
        Directory.CreateDirectory(_root);
        var sessions = new DiagramViewportRegistry();
        _provider = new ServiceCollection()
            .AddSingleton<IDiagramViewportRegistry>(sessions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddCausalLoop()
            .BuildServiceProvider();
        _actions = new CausalLoopContextActionProvider(_store, _provider.GetRequiredService<IHistoryStackStore>(), sessions);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AVariableWithAnEmptyId_IsReportedAsAProblem_NotDeclared()
    {
        var parsed = CausalLoopParser.Parse(CausalLoopDocument.Parse(WithEmptyVariable));

        Assert.Contains(parsed.Problems, problem => problem.Lines.Start == 2);
        Assert.False(parsed.Model.Declares(""), "A variable with an empty id was declared.");
        Assert.True(parsed.Model.Declares("a"));
    }

    [Theory]
    [InlineData("rel:a->")]
    [InlineData("rel:->b")]
    [InlineData("rel:->")]
    public void ARelationIdWithAnEmptyEnd_IsRefused(string elementId)
    {
        Assert.Null(CausalLoopSelection.RelationOf(elementId));
    }

    [Fact]
    public void ARelationIdWithBothEnds_StillReads()
    {
        Assert.Equal(("a", "b"), CausalLoopSelection.RelationOf("rel:a->b"));
    }

    [Fact]
    public async Task AConnectToAnEmptyEnd_OnADocumentWithAnEmptyVariable_LeavesTheFileAlone()
    {
        // End to end, through the gesture a right-drag raises: whatever the provider answers, the
        // file must not gain a link with a missing end.
        var path = IoPath.Combine(_root, "nameless.cld");
        await File.WriteAllTextAsync(path, WithEmptyVariable, TestContext.Current.CancellationToken);
        var target = new ContextTarget(ContextScope.DiagramElement, path, false, ShortGuid.NewShortGuid(), _root, default, "rel:a->");

        // Any answer short of a written link is acceptable; only the file is measured.
        await _actions.ExecuteAsync(target, CausalLoopContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        Assert.Equal(WithEmptyVariable, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}
