using EtAlii.Adp.Diagram.C4.Tests.Parity;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Every position the two examples keep in their <c>*.layout.json</c> is applied by <see cref="C4Positions"/> as
/// <see cref="C4LayoutSidecar"/>, the store it replaced, read it - per view, through every registration that opens the
/// view as well as with none, and with no file rewritten to make it so (c4-disl-fbl task 16, Requirement 7.1).
/// </summary>
public sealed class C4ExamplePositionsTests
{
    private static string ExamplesRoot { get; } = RepositoryPaths.Under("src", "diagrams", "c4", "examples");

    public static TheoryData<string> EveryProject() => ["reference", "industrial-plant"];

    [Theory]
    [MemberData(nameof(EveryProject))]
    public async Task EveryStoredPosition_IsReadAsTheSidecarReadIt_InEveryView(string project)
    {
        // Arrange.
        var body = Directory.GetFiles(IoPath.Combine(ExamplesRoot, project), "*.dsl", SearchOption.AllDirectories).Single();
        var layout = C4Positions.LegacyPathFor(body);
        Assert.True(File.Exists(layout), $"{layout} is the layout file this test compares");
        var layoutBefore = await File.ReadAllBytesAsync(layout, TestContext.Current.CancellationToken);
        var workspace = C4Body.Read(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).Workspace;
        var ids = workspace.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var oracle = new C4LayoutSidecar(new FileWriter());
        var positions = new C4Positions(new FileWriter());
        var registrations = Directory.GetFiles(IoPath.GetDirectoryName(body)!, "*.adp")
            .Select(path => (Path: path, Registration: AdpRegistration.Read(path)))
            .ToList();

        // Act and assert.
        var compared = 0;
        foreach (var view in workspace.Views)
        {
            var expected = oracle.Read(body, view.Key);
            var openers = registrations.Where(pair => pair.Registration?.FirstView == view.Key).Select(pair => (string?)pair.Path).Prepend(null);
            foreach (var registration in openers)
            {
                var read = positions.Read(body, registration, view.Key, ids);
                Assert.Equal(expected.Count, read.Count);
                foreach ((string key, C4SidecarPosition position) in expected)
                {
                    Assert.True(read.TryGetValue(key, out var applied), $"{key} on {view.Key} through {registration ?? "no registration"}");
                    Assert.Equal(new C4Position(position.X, position.Y), applied);
                    compared++;
                }
            }
        }

        // Something was compared, and reading rewrote nothing.
        Assert.True(compared > 0, $"{project} stores no position to compare");
        Assert.Equal(layoutBefore, await File.ReadAllBytesAsync(layout, TestContext.Current.CancellationToken));
    }
}
