using EtAlii.Adp.Context;
using EtAlii.Adp.Specification.Disl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones, row for row
/// (ansible-structure-disl-fbl, task 5, Requirements 3.1, 3.4 and 3.6): for every folder of the parity
/// corpus, every node, every relation and an unknown id. A difference names the folder, the element and
/// both rows. The refusal of a reparenting move is the hand-written sentence, and the definition offers
/// no tool and no context action.
/// </summary>
/// <remarks>
/// The corpus, and what is made in each copy where git cannot hold it, are the transcript's
/// (<see cref="AnsibleTranscript"/>), so this compares what the transcript records, and names the element
/// where the transcript would name only a line.
/// </remarks>
public class AnsibleDerivedRowsTests
{
    private const string Unknown = "parity:unknown";

    public static TheoryData<string> Folders() => [.. AnsibleTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryRow_IsTheHandWrittenOne(string folder)
    {
        using var copy = Copy(folder);

        // Arrange.
        var project = new AnsibleProjectReader().Read(Made(copy));
        var graph = AnsibleGraph.Derive(project);
        List<string> targets = [.. graph.Nodes.Select(node => node.Id), .. graph.Edges.Select(edge => edge.Id), Unknown];

        // Act and assert.
        foreach (var target in targets)
        {
            var expected = HandWrittenAnsible.Rows(project, target).Select(row => row.RowLine()).ToList();
            var actual = AnsibleModels.Rows(project, target).Select(row => row.RowLine()).ToList();
            Assert.True(
                expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"{folder} {target}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    [Fact]
    public void AReparentingMove_IsRefusedWithTheHandWrittenSentence() =>
        Assert.Equal(HandWrittenAnsible.MoveRefusal, AnsibleDefinition.MoveRefusal);

    /// <summary>The sentence a property write is refused with is the binding's <c>readOnly</c>, and it is the hand-written one (finding A6).</summary>
    [Fact]
    public async Task AWrite_IsRefusedWithTheHandWrittenSentence()
    {
        // Arrange.
        using var folders = new AnsibleFolders();
        var target = ContextTarget.ForElement(Path.Combine(Path.GetTempPath(), "parity.adp"), "role:nginx", ProjectRoot.From(Path.GetTempPath()));

        // Act.
        var result = await new AnsibleContextPropertyProvider(folders).SetAsync(target, "ansible.name", "renamed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(HandWrittenAnsible.Refusal, result.Error);
    }

    [Fact]
    public void TheToolbox_IsEmpty()
    {
        // Act.
        var derived = ToolboxDerivation.Derive(AnsibleDefinition.Specification, AnsibleDefinition.Ids);
        using var provider = new ServiceCollection().AddAnsibleStructure().BuildServiceProvider();
        var answered = provider.GetServices<IDiagramToolboxProvider>()
            .Where(toolbox => toolbox.Origin == Diagram.AnsibleStructure.Origin)
            .SelectMany(toolbox => toolbox.Items);

        // Assert.
        Assert.Empty(derived);
        Assert.Empty(answered);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void NoContextAction_IsOffered(string folder)
    {
        using var copy = Copy(folder);

        // Arrange.
        var model = AnsibleModels.ModelOf(new AnsibleProjectReader().Read(Made(copy)));
        using var provider = new ServiceCollection().AddAnsibleStructure().BuildServiceProvider();

        // Act.
        var offered = model.Elements
            .Select(DislMenuTarget.Element)
            .Append(DislMenuTarget.Canvas(model))
            .SelectMany(target => ContextMenuDerivation.Derive(AnsibleDefinition.Specification, target, new DislEnv(ReadOnly: true), AnsibleDefinition.Ids))
            .ToList();

        // Assert.
        Assert.Empty(offered);
        Assert.Empty(provider.GetServices<IContextActionProvider>());
    }

    /// <summary>
    /// A temporary folder holding <paramref name="folder"/>, a path relative to <c>src/</c>, made as the transcript
    /// makes it, under <see cref="Made"/>: beneath the temporary folder, so that one the making takes away is still read
    /// as not there.
    /// </summary>
    private static TestFolder Copy(string folder)
    {
        var copy = TestFolder.Create("EtAlii.Adp.AnsibleDerivedRows");
        AnsibleTranscript.MakeCopy(folder, Made(copy));
        return copy;
    }

    private static string Made(TestFolder copy) => copy.Combine("folder");
}
