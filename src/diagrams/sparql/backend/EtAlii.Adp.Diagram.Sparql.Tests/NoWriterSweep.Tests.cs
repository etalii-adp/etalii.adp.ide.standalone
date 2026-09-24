using System.Reflection;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The second layer of the no-writer proof, and the one that verifies the guarantee whole: a
/// vendored query is byte-snapshotted, then every surface this module offers is exercised
/// against it - a full render, every action the provider yields, a property set, a reposition,
/// its undo, and an external-edit reload - and the <c>.rq</c> is asserted byte-identical
/// afterwards, with only the <c>.adp</c> changed.
/// </summary>
/// <remarks>
/// The other two layers live where they guard best: the reflection surface test with the parser
/// (<see cref="NoWriterSurfaceTests"/>, so it guards from the module's first merge), and the
/// registration test with the providers. The meta-assertion at the bottom of this file fails if
/// any of the three stops existing, because a three-layer guarantee with one layer quietly
/// deleted is a one-layer guarantee that still reads like three.
/// </remarks>
public class NoWriterSweepTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public NoWriterSweepTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddSparql()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    /// <summary>A real vendored query, copied out of the examples tree the module ships.</summary>
    private string CopyVendoredExample()
    {
        var examples = ExampleCorpusTests.ExamplesRoot();
        var source = IoPath.Combine(examples, "w3c-sparql", "optional.rq");
        var destination = IoPath.Combine(_root, "optional.rq");
        File.Copy(source, destination);
        File.WriteAllText(IoPath.Combine(_root, "query.adp"), "w3c/sparql\r\nbody: optional.rq\r\n");
        return destination;
    }

    [Fact]
    public async Task EverySurfaceThisModuleOffers_LeavesTheQueryByteIdentical()
    {
        // Arrange: the bytes as vendored, and the registration as Add would have written it.
        var body = CopyVendoredExample();
        var registration = IoPath.Combine(_root, "query.adp");
        var queryBefore = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var registrationBefore = await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken);

        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);
        var target = new ContextTarget(
            ContextScope.DiagramElement, body, IsContainer: false, SourceId: default, _root, ShortGuid.NewShortGuid(), "var:x");

        // Act: everything the module can be asked to do, in one pass.
        await using (var session = factory.Open(ShortGuid.NewShortGuid(), _root, body, registration))
        {
            // 1. A full render.
            var baseline = Assert.Single(session.Baseline());
            Assert.NotEmpty(Assert.IsType<DiagramAddDelta>(baseline).Elements);

            // 2. Every action the provider offers, executed - expected: there are none, so this
            //    loop is empty, which is the finding rather than a gap in the test.
            var actions = _provider.GetServices<IContextActionProvider>()
                .OfType<SparqlContextActionProvider>()
                .Single();
            foreach (var group in await actions.DiscoverAsync(target, CancellationToken.None))
            {
                foreach (var action in group.Actions)
                {
                    await actions.ExecuteAsync(target, action.Id, CancellationToken.None);
                    await actions.CommitAsync(target, action.Id, "value", "", CancellationToken.None);
                }
            }

            // 3. A property set, which the provider refuses.
            var properties = _provider.GetServices<IContextPropertyProvider>()
                .OfType<SparqlContextPropertyProvider>()
                .Single();
            foreach (var row in await properties.DescribeAsync(target, CancellationToken.None))
            {
                await properties.SetAsync(target, row.Id, "something else", CancellationToken.None);
            }

            // 4. A reposition - the one gesture that does anything at all.
            Assert.Equal("", await session.MoveElementToAsync("var:name", 400, 300, CancellationToken.None));

            // 5. Its undo.
            await _provider.GetRequiredService<IHistoryStackStore>().Get(_root).UndoAsync(CancellationToken.None);

            // 6. An external edit, picked up through the reload seam.
            await File.WriteAllTextAsync(body, (await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).Replace("?mbox", "?mailbox", StringComparison.Ordinal), TestContext.Current.CancellationToken);
            _provider.GetServices<IDiagramDocumentReloader>()
                .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin)
                .Reload(_root, body);
        }

        // Assert: the only bytes that differ are the ones this test wrote itself in step 6, so
        // the file still holds exactly the vendored query with that one rename applied - no
        // reformatting, no reserialization, nothing the module added on its way past.
        var expected = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(queryBefore).Replace("?mbox", "?mailbox", StringComparison.Ordinal));
        Assert.Equal(expected, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));

        // And the registration came back to exactly what it was, because undo restores bytes.
        Assert.Equal(registrationBefore, await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARepositionTouchesTheRegistrationAlone()
    {
        // Arrange.
        var body = CopyVendoredExample();
        var registration = IoPath.Combine(_root, "query.adp");
        var queryBefore = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);

        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), _root, body, registration);
        session.Baseline();
        await session.MoveElementToAsync("var:name", 120, 80, CancellationToken.None);

        // Assert: the query is untouched to the byte, and the position landed in the .adp.
        Assert.Equal(queryBefore, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        Assert.Contains("var:name: 120 80", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AllThreeNoWriterLayers_ExistAndRunInThisSuite()
    {
        // Arrange: the meta-assertion. Each layer is named by the test that carries it, so
        // deleting one fails here rather than silently weakening the guarantee.
        var assembly = typeof(NoWriterSweepTests).Assembly;

        // Act & assert.
        AssertTestExists(assembly, typeof(NoWriterSurfaceTests), "TheModuleDeclaresNoCommandAndNoWriter");
        AssertTestExists(assembly, typeof(NoWriterSurfaceTests), "TheStoreSurface_EndsAtReading");
        AssertTestExists(assembly, typeof(NoWriterSweepTests), "EverySurfaceThisModuleOffers_LeavesTheQueryByteIdentical");
        AssertTestExists(assembly, typeof(DiagramTests), "TheModule_RegistersNoToolboxAndNoCommands");
    }

    private static void AssertTestExists(Assembly assembly, Type type, string method)
    {
        var found = assembly.GetType(type.FullName!)?.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(
            found is not null && found.GetCustomAttributes().Any(attribute => attribute is FactAttribute),
            $"The no-writer proof lost a layer: {type.Name}.{method} is gone or is no longer a test.");
    }
}
