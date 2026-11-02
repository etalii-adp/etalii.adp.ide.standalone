using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// Round trip and undo are byte-exact for the corpus (spec 071, T012): a document saved without a change is planned as no
/// splice and keeps its bytes, and every edit undone gives the bytes back.
/// </summary>
public sealed class WardleyRoundTripTests
{
    public static TheoryData<string> Names() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public async Task ADocumentSavedWithoutAChange_IsNoSplice_AndKeepsItsBytes(string name)
    {
        // Arrange.
        using var session = await WardleyCorpus.OpenAsync(name, TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(session.Body, TestContext.Current.CancellationToken);
        var entry = session.Store.Get(session.Body)!;

        // Act.
        var edit = WardleyBody.Edit(entry.Document, session.Store.Identities(session.Body));
        await session.Store.SaveAsync(session.Body, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(edit.Splices);
        Assert.Equal(entry.Document.Text, edit.Text);
        Assert.Equal(before, await File.ReadAllBytesAsync(session.Body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryEditUndone_GivesTheBytesBack(string name)
    {
        // Arrange.
        using var session = await WardleyCorpus.OpenAsync(name, TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(session.Body, TestContext.Current.CancellationToken);
        var components = session.Store.Identities(session.Body).Where(entry => entry.Kind == WardleyIdentityKind.Component).Select(entry => entry.Id).ToList();

        // Act and assert: each edit on its own, from the document as it was opened.
        var undone = 0;
        foreach (var command in Edits(session.Body, components))
        {
            var done = await session.Dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
            if (!done.IsSuccess) continue;
            if (done.Inverse is null)
            {
                // An edit that changed nothing has nothing to undo, and wrote nothing.
                Assert.Equal(before, await File.ReadAllBytesAsync(session.Body, TestContext.Current.CancellationToken));
                continue;
            }

            var restored = await session.Dispatcher.DispatchAsync(done.Inverse, TestContext.Current.CancellationToken);
            Assert.True(restored.IsSuccess, restored.Error);
            Assert.Equal(before, await File.ReadAllBytesAsync(session.Body, TestContext.Current.CancellationToken));
            undone++;
        }

        Assert.True(components.Count == 0 || undone > 0, $"No edit of {name} was written.");
    }

    /// <summary>A move, a rename, an inertia toggle, a link, an evolution and a removal of each component, and an add.</summary>
    private static IEnumerable<ICommand> Edits(string body, IReadOnlyList<string> components)
    {
        yield return new AddWardleyElementCommand(body, "component", "Round Trip", 0.25d, 0.25d);
        yield return new AddWardleyNoteCommand(body, "Round trip", 0.75d, 0.75d);
        for (var i = 0; i < components.Count; i++)
        {
            var id = components[i];
            yield return new MoveWardleyElementCommand(body, id, 0.4321d, 0.1234d);
            yield return new RenameWardleyElementCommand(body, id, "Renamed Round Trip");
            yield return new SetWardleyInertiaCommand(body, id, Inertia: true);
            yield return new SetWardleyEvolveCommand(body, id, Present: true, 0.95d);
            yield return new SetWardleyLinkCommand(body, id, components[(i + 1) % components.Count], WardleyLinkKind.Dependency, Present: true);
            yield return new RemoveWardleyElementCommand(body, id);
        }
    }
}
