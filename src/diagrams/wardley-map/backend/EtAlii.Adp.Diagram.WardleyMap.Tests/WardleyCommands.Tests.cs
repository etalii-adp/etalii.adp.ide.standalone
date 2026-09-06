using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The module's commands, against the real history stack. Every one of these asserts what
/// happened to the FILE, because that is what a command is for.
/// </summary>
public sealed class WardleyCommandsTests : IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-commands-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly IHistoryStack _history;
    private readonly string _path;

    public WardleyCommandsTests()
    {
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCommands().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
        _path = IoPath.Combine(_root, "map.owm");
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private void Write(string text) => File.WriteAllText(_path, text);

    private string Read() => File.ReadAllText(_path);

    /// <summary>The element id the session would hand out for a named component.</summary>
    private string IdOf(string name)
    {
        var entry = _documents
            .Identities(_path)
            .Single(candidate => candidate.Kind == WardleyIdentityKind.Component && candidate.Key == name);
        return entry.Id;
    }

    private Task<CommandResult> Execute(ICommand command) =>
        _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Add_AppendsAStatementAndLeavesTheRestAlone()
    {
        // Arrange.
        Write("// kept\ntitle Tea\ncomponent Alpha [0.5, 0.5]\n");

        // Act.
        var result = await Execute(new AddWardleyElementCommand(_path, "component", "Beta", 0.8d, 0.2d));

        // Assert. New statements go at the end: the DSL is order-independent, and inserting
        // into the middle rearranges a layout the author chose.
        Assert.True(result.IsSuccess);
        Assert.Equal("// kept\ntitle Tea\ncomponent Alpha [0.5, 0.5]\ncomponent Beta [0.8, 0.2]\n", Read());
    }

    [Fact]
    public async Task Add_RefusesADuplicateName()
    {
        // Arrange. Requirement 14.4 reports two components sharing a name as an error, so
        // refusing is kinder than writing it and reporting it a moment later.
        Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var result = await Execute(new AddWardleyElementCommand(_path, "component", "Alpha", 0.1d, 0.1d));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already has an element", result.Error, StringComparison.Ordinal);
        Assert.Equal("component Alpha [0.5, 0.5]\n", Read());
    }

    [Fact]
    public async Task Add_RefusesAStatementKindTheFormatDoesNotHave()
    {
        // Arrange. `market` reads like a kind and is a decorator.
        Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var result = await Execute(new AddWardleyElementCommand(_path, "market", "Beta", 0.5d, 0.5d));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no 'market' statement", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_ClampsAPositionOutsideTheMap()
    {
        // Arrange.
        Write("title Empty\n");

        // Act.
        await Execute(new AddWardleyElementCommand(_path, "component", "Alpha", 1.8d, -0.3d));

        // Assert.
        Assert.Contains("component Alpha [1, 0]", Read(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_IsUndoneExactly()
    {
        // Arrange.
        const string before = "// kept\ncomponent Alpha [0.50, 0.50]\n";
        Write(before);

        // Act.
        await Execute(new AddWardleyElementCommand(_path, "component", "Beta", 0.1d, 0.1d));
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert. Byte-identical, trailing-zero formatting included.
        Assert.Equal(before, Read());
    }

    [Fact]
    public async Task Remove_TakesEveryStatementThatNamedTheElement()
    {
        // Arrange. Removing the declaration alone would leave links pointing at a name nothing
        // declares - a document ADP made invalid.
        Write("""
            component Alpha [0.5, 0.5]
            component Beta [0.2, 0.2]
            Alpha->Beta
            Beta->Alpha
            evolve Alpha 0.9

            """);
        _documents.GetOrLoad(_path);

        // Act.
        var result = await Execute(new RemoveWardleyElementCommand(_path, IdOf("Alpha")));

        // Assert.
        Assert.True(result.IsSuccess);
        var after = Read();
        Assert.DoesNotContain("Alpha", after, StringComparison.Ordinal);
        Assert.Contains("component Beta [0.2, 0.2]", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_IsUndoneExactly()
    {
        // Arrange.
        const string before = "component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha->Beta\n";
        Write(before);
        _documents.GetOrLoad(_path);

        // Act.
        await Execute(new RemoveWardleyElementCommand(_path, IdOf("Alpha")));
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert. Requirement 9.6 - every statement the remove took comes back together.
        Assert.Equal(before, Read());
    }

    [Fact]
    public async Task Rename_RewritesTheDeclarationAndEveryReference()
    {
        // Arrange. Requirement 4.4.
        Write("component Kettle [0.43, 0.35]\nCup of Tea->Kettle\nevolve Kettle 0.62\n");
        _documents.GetOrLoad(_path);

        // Act.
        var result = await Execute(new RenameWardleyElementCommand(_path, IdOf("Kettle"), "Boiler"));

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("component Boiler [0.43, 0.35]\nCup of Tea->Boiler\nevolve Boiler 0.62\n", Read());
    }

    [Fact]
    public async Task Rename_KeepsTheElementsIdentity()
    {
        // Arrange. Requirement 4.1 - a rename is an edit, not a delete plus an add, so the
        // selection and every undo entry naming it survive.
        Write("component Kettle [0.43, 0.35]\n");
        _documents.GetOrLoad(_path);
        var before = IdOf("Kettle");

        // Act.
        await Execute(new RenameWardleyElementCommand(_path, before, "Boiler"));

        // Assert.
        Assert.Equal(before, IdOf("Boiler"));
    }

    [Fact]
    public async Task Rename_IsUndoneExactly()
    {
        // Arrange.
        const string before = "component Kettle [0.43, 0.35]\nCup of Tea->Kettle\nevolve Kettle 0.62\n";
        Write(before);
        _documents.GetOrLoad(_path);

        // Act.
        await Execute(new RenameWardleyElementCommand(_path, IdOf("Kettle"), "Boiler"));
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert. Requirement 9.6 - never a partially renamed file.
        Assert.Equal(before, Read());
    }

    [Fact]
    public async Task Rename_RefusesANameAlreadyTaken()
    {
        // Arrange.
        Write("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\n");
        _documents.GetOrLoad(_path);

        // Act.
        var result = await Execute(new RenameWardleyElementCommand(_path, IdOf("Alpha"), "Beta"));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already has an element", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetInertia_AddsAndRemovesTheBareWord()
    {
        // Arrange. Requirement 6.2 - inertia is a boolean, not a decorator.
        Write("component Datacentre [0.15, 0.55]\n");
        _documents.GetOrLoad(_path);
        var id = IdOf("Datacentre");

        // Act and assert.
        await Execute(new SetWardleyInertiaCommand(_path, id, true));
        Assert.Equal("component Datacentre [0.15, 0.55] inertia\n", Read());

        await Execute(new SetWardleyInertiaCommand(_path, id, false));
        Assert.Equal("component Datacentre [0.15, 0.55]\n", Read());
    }

    [Fact]
    public async Task SetDecorator_AddsOneWithoutDisturbingAnother()
    {
        // Arrange. All five live in one set, so setting one must leave the others alone.
        Write("component Payment [0.70, 0.72] (buy)\n");
        _documents.GetOrLoad(_path);

        // Act.
        await Execute(new SetWardleyDecoratorCommand(_path, IdOf("Payment"), WardleyDecorator.Market, true));

        // Assert.
        Assert.Equal("component Payment [0.70, 0.72] (buy) (market)\n", Read());
    }

    [Fact]
    public async Task SetDecorator_RemovesOneAndTakesItsSpacingWithIt()
    {
        // Arrange.
        Write("component Payment [0.70, 0.72] (buy)\n");
        _documents.GetOrLoad(_path);

        // Act.
        await Execute(new SetWardleyDecoratorCommand(_path, IdOf("Payment"), WardleyDecorator.Buy, false));

        // Assert. No trailing space left behind.
        Assert.Equal("component Payment [0.70, 0.72]\n", Read());
    }

    [Fact]
    public async Task SetDecorator_KeepsATrailingComment()
    {
        // Arrange.
        Write("component Payment [0.70, 0.72] // why\n");
        _documents.GetOrLoad(_path);

        // Act.
        await Execute(new SetWardleyDecoratorCommand(_path, IdOf("Payment"), WardleyDecorator.Buy, true));

        // Assert.
        Assert.Equal("component Payment [0.70, 0.72] (buy) // why\n", Read());
    }

    [Fact]
    public async Task SetDecorator_IsIdempotentWithoutWritingAnEmptyUndoEntry()
    {
        // Arrange.
        Write("component Payment [0.70, 0.72] (buy)\n");
        _documents.GetOrLoad(_path);

        // Act. Already in the state asked for.
        var result = await Execute(new SetWardleyDecoratorCommand(_path, IdOf("Payment"), WardleyDecorator.Buy, true));

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("component Payment [0.70, 0.72] (buy)\n", Read());
    }

    // ---- links --------------------------------------------------------------------------------

    [Fact]
    public async Task SetLink_AppendsTheArrowItWasAskedFor()
    {
        // Arrange.
        Write("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\n");

        // Act.
        var result = await Execute(new SetWardleyLinkCommand(
            _path, IdOf("Alpha"), IdOf("Beta"), WardleyLinkKind.Flow, Present: true, "cash"));

        // Assert. A flow link is written with its own arrow, and its context after a semicolon.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha+>Beta; cash\n", Read());
    }

    [Fact]
    public async Task SetLink_RewritesAnExistingLink_RatherThanAddingASecond()
    {
        // Arrange. Two statements saying the same thing is what Requirement 14.4 reports, so
        // setting a link that already exists changes the one that is there.
        Write("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha->Beta\n");

        // Act.
        var result = await Execute(new SetWardleyLinkCommand(
            _path, IdOf("Alpha"), IdOf("Beta"), WardleyLinkKind.Dependency, Present: true, "needs"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha->Beta; needs\n", Read());
    }

    [Fact]
    public async Task SetLink_ClearingOneKind_LeavesTheOtherArrowBetweenThePairAlone()
    {
        // Arrange. The DSL lets one pair carry a dependency and a flow at once; they are two
        // different claims, so the kind is part of what identifies the link.
        Write("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha->Beta\nAlpha+>Beta\n");

        // Act.
        var result = await Execute(new SetWardleyLinkCommand(
            _path, IdOf("Alpha"), IdOf("Beta"), WardleyLinkKind.Dependency, Present: false));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\nAlpha+>Beta\n", Read());
    }

    [Fact]
    public async Task SetLink_RefusesToLinkAnElementToItself()
    {
        // Arrange.
        Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var result = await Execute(new SetWardleyLinkCommand(
            _path, IdOf("Alpha"), IdOf("Alpha"), WardleyLinkKind.Dependency, Present: true));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("two different elements", result.Error, StringComparison.Ordinal);
        Assert.Equal("component Alpha [0.5, 0.5]\n", Read());
    }

    [Fact]
    public async Task SetLink_RefusesToClearALinkThatIsNotThere()
    {
        // Arrange. Requirement 11.6 - the menu should not offer this, and if it does the
        // command says why rather than reporting a success that changed nothing.
        Write("component Alpha [0.5, 0.5]\ncomponent Beta [0.2, 0.2]\n");

        // Act.
        var result = await Execute(new SetWardleyLinkCommand(
            _path, IdOf("Alpha"), IdOf("Beta"), WardleyLinkKind.Dependency, Present: false));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no such link", result.Error, StringComparison.Ordinal);
    }

    // ---- evolve -------------------------------------------------------------------------------

    [Fact]
    public async Task SetEvolve_AppendsTheStatement_AndThenRewritesItInPlace()
    {
        // Arrange.
        Write("component Alpha [0.5, 0.5]\n");

        // Act.
        Assert.True((await Execute(new SetWardleyEvolveCommand(_path, IdOf("Alpha"), Present: true, 0.7d))).IsSuccess);
        Assert.Equal("component Alpha [0.5, 0.5]\nevolve Alpha 0.7\n", Read());

        var again = await Execute(new SetWardleyEvolveCommand(_path, IdOf("Alpha"), Present: true, 0.85d));

        // Assert. A component evolves to one place, so a second target replaces the first.
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal("component Alpha [0.5, 0.5]\nevolve Alpha 0.85\n", Read());
    }

    [Fact]
    public async Task SetEvolve_WritesTheNameTheComponentTakesWhenItArrives()
    {
        // Arrange. The `evolve Name->NewName x` form, which is a rename that happens on arrival
        // rather than now.
        Write("component Kettle [0.4, 0.3]\n");

        // Act.
        var result = await Execute(new SetWardleyEvolveCommand(
            _path, IdOf("Kettle"), Present: true, 0.75d, "Electric Kettle"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("component Kettle [0.4, 0.3]\nevolve Kettle->Electric Kettle 0.75\n", Read());
    }

    [Fact]
    public async Task SetEvolve_ClampsPastTheEndOfTheAxis()
    {
        // Arrange. The same judgement a drag makes: the axis has ends, and a value past one is
        // a slip rather than a different intention (Requirement 7.3).
        Write("component Alpha [0.5, 0.5]\n");

        // Act.
        await Execute(new SetWardleyEvolveCommand(_path, IdOf("Alpha"), Present: true, 1.4d));

        // Assert.
        Assert.Equal("component Alpha [0.5, 0.5]\nevolve Alpha 1\n", Read());
    }

    [Fact]
    public async Task SetEvolve_ClearsTheStatement_AndRefusesWhenThereIsNone()
    {
        // Arrange.
        Write("component Alpha [0.5, 0.5]\nevolve Alpha 0.7\n");

        // Act.
        var cleared = await Execute(new SetWardleyEvolveCommand(_path, IdOf("Alpha"), Present: false, 0d));
        var again = await Execute(new SetWardleyEvolveCommand(_path, IdOf("Alpha"), Present: false, 0d));

        // Assert.
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Equal("component Alpha [0.5, 0.5]\n", Read());
        Assert.False(again.IsSuccess);
        Assert.Contains("is not evolving", again.Error, StringComparison.Ordinal);
    }

    // ---- pipeline membership ------------------------------------------------------------------

    [Fact]
    public async Task PipelineMembership_GivesTheParentABlockWhenItHasNone()
    {
        // Arrange.
        Write("component Kettle [0.4, 0.3]\n");

        // Act.
        var result = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Kettle"), "Electric Kettle", Member: true, 0.63d));

        // Assert. Written in the nested form, because that is the form that can hold a child.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Electric Kettle [0.63]\n}\n",
            Read());
    }

    [Fact]
    public async Task PipelineMembership_InsertsBeforeTheClosingBrace_LeavingWhatIsInTheBlockAlone()
    {
        // Arrange. A block may hold comments and blank lines, which Requirement 3.3 says
        // survive untouched - so the brace is found by looking rather than by counting children.
        Write("component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Campfire Kettle [0.15]\n  // the other one\n}\n");

        // Act.
        var result = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Kettle"), "Electric Kettle", Member: true, 0.63d));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Campfire Kettle [0.15]\n  // the other one\n  component Electric Kettle [0.63]\n}\n",
            Read());
    }

    [Fact]
    public async Task PipelineMembership_RemovesTheChildItNames()
    {
        // Arrange.
        Write("component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Campfire Kettle [0.15]\n  component Electric Kettle [0.63]\n}\n");

        // Act.
        var result = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Kettle"), "Campfire Kettle", Member: false));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Electric Kettle [0.63]\n}\n",
            Read());
    }

    [Fact]
    public async Task PipelineMembership_RefusesTheLegacyTwoCoordinateForm()
    {
        // Arrange. Requirement 3.2 - a map written in the legacy form is written back that way,
        // so the command refuses rather than silently converting the statement.
        Write("component Power [0.1, 0.7]\npipeline Power [0.30, 0.85]\n");

        // Act.
        var result = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Power"), "Solar", Member: true, 0.4d));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("older two-coordinate form", result.Error, StringComparison.Ordinal);
        Assert.Equal("component Power [0.1, 0.7]\npipeline Power [0.30, 0.85]\n", Read());
    }

    [Fact]
    public async Task PipelineMembership_RefusesADuplicateChild_AndAChildThatIsNotThere()
    {
        // Arrange.
        Write("component Kettle [0.4, 0.3]\npipeline Kettle\n{\n  component Electric Kettle [0.63]\n}\n");

        // Act.
        var duplicate = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Kettle"), "Electric Kettle", Member: true, 0.4d));
        var absent = await Execute(new SetWardleyPipelineMembershipCommand(
            _path, IdOf("Kettle"), "Campfire Kettle", Member: false));

        // Assert.
        Assert.False(duplicate.IsSuccess);
        Assert.Contains("already in this pipeline", duplicate.Error, StringComparison.Ordinal);
        Assert.False(absent.IsSuccess);
        Assert.Contains("is not in this pipeline", absent.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryEditRefusesAnElementThatIsNotThere()
    {
        // Arrange. Requirement 9.4 - a rejection carries a message, never an exception, and
        // Requirement 9.5 checks against current state because undo dispatches again later.
        Write("component Alpha [0.5, 0.5]\n");

        // Act and assert.
        foreach (var command in new ICommand[]
        {
            new RemoveWardleyElementCommand(_path, "gone"),
            new RenameWardleyElementCommand(_path, "gone", "Beta"),
            new SetWardleyInertiaCommand(_path, "gone", true),
            new SetWardleyDecoratorCommand(_path, "gone", WardleyDecorator.Buy, true),
            new SetWardleyLinkCommand(_path, "gone", IdOf("Alpha"), WardleyLinkKind.Dependency, true),
            new SetWardleyEvolveCommand(_path, "gone", true, 0.5d),
            new SetWardleyPipelineMembershipCommand(_path, "gone", "Child", true),
        })
        {
            var result = await Execute(command);
            Assert.False(result.IsSuccess);
            Assert.Equal("That element is no longer on this map.", result.Error);
        }

        Assert.Equal("component Alpha [0.5, 0.5]\n", Read());
    }

    [Fact]
    public async Task AnEditAndItsUndoLeaveTheDocumentByteIdentical_AcrossTheWholeSet()
    {
        // Arrange. The property that matters most: a user who edits and immediately undoes must
        // be left with the file they started with, whatever the edit was (Requirements 3.1, 9.6).
        const string before = "// a comment\ncomponent Alpha [0.50, 0.50] (buy)\ncomponent Beta [0.20, 0.20]\nAlpha->Beta\n";

        foreach (var make in new Func<string, ICommand>[]
        {
            _ => new AddWardleyElementCommand(_path, "component", "Gamma", 0.3d, 0.3d),
            id => new RemoveWardleyElementCommand(_path, id),
            id => new RenameWardleyElementCommand(_path, id, "Renamed"),
            id => new SetWardleyInertiaCommand(_path, id, true),
            id => new SetWardleyDecoratorCommand(_path, id, WardleyDecorator.Market, true),
            id => new SetWardleyDecoratorCommand(_path, id, WardleyDecorator.Buy, false),
            id => new SetWardleyLinkCommand(_path, id, IdOf("Beta"), WardleyLinkKind.Flow, true, "cash"),
            id => new SetWardleyLinkCommand(_path, id, IdOf("Beta"), WardleyLinkKind.Dependency, false),
            id => new SetWardleyEvolveCommand(_path, id, true, 0.9d),
            id => new SetWardleyPipelineMembershipCommand(_path, id, "Child", true, 0.4d),
        })
        {
            Write(before);
            _documents.Forget(_path);
            _documents.GetOrLoad(_path);

            // Act.
            var result = await Execute(make(IdOf("Alpha")));
            Assert.True(result.IsSuccess, result.Error);
            await _history.UndoAsync(TestContext.Current.CancellationToken);

            // Assert.
            Assert.Equal(before, Read());
        }
    }

    [Fact]
    public async Task AWriteThatCannotLand_IsReportedRatherThanAnsweredWithSuccess()
    {
        // Arrange: a holder that shares Read only, which denies the replace a publish performs.
        // An external editor with the file open looks exactly like this to us.
        Write("component Alpha [0.5, 0.5]\n");
        var id = IdOf("Alpha");
        using var holder = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act.
        var result = await Execute(new MoveWardleyElementCommand(_path, id, 0.8d, 0.2d));

        // Assert: the caller is TOLD. Answering success while the file still holds the old
        // position is worse than failing - the user believes the move landed and it did not.
        Assert.False(result.IsSuccess);
        Assert.Contains("could not be written", result.Error, StringComparison.Ordinal);
        Assert.Equal("component Alpha [0.5, 0.5]\n", Read());
    }

    [Fact]
    public async Task AnEditWhoseIdentitiesCannotBeSaved_SucceedsButSaysSo()
    {
        // Arrange: one edit so the identity sidecar exists, then hold it so it cannot be
        // replaced. A missing sidecar is re-derived and succeeds, so it has to exist here.
        Write("component Alpha [0.5, 0.5]\n");
        await Execute(new AddWardleyElementCommand(_path, "component", "Beta", 0.8d, 0.2d));
        var sidecarPath = WardleyIdentities.PathFor(_path);
        Assert.True(File.Exists(sidecarPath), $"expected a sidecar at {sidecarPath}");
        using var holder = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act.
        var result = await Execute(new AddWardleyElementCommand(_path, "component", "Gamma", 0.3d, 0.7d));

        // Assert: the map itself was written, so the command succeeds - the element is there and
        // refusing it would lose real work. The identities were not, which costs stable ids on
        // the next open, so the user is told instead of finding out then.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("component Gamma", Read(), StringComparison.Ordinal);
        Assert.NotEqual("", result.Warning);
        Assert.Contains("new ids", result.Warning, StringComparison.Ordinal);
    }
}
