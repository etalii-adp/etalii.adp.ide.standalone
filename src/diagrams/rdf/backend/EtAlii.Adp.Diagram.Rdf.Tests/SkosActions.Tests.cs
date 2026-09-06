using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The thesaurus gestures through the family provider trio (skos-diagram Requirements 5, 6):
/// discovery keyed off the file's own assertions, each gesture's smallest diff, byte-restoring
/// undo through the real pipeline, and the refusals with their sentences.
/// </summary>
public class SkosActionsTests : IDisposable
{
    private const string Vocabulary =
        "@prefix skos: <http://www.w3.org/2004/02/skos/core#> .\r\n"
        + "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "ex:scheme a skos:ConceptScheme ; skos:prefLabel \"Drinks\"@en ; skos:hasTopConcept ex:tea .\r\n"
        + "ex:tea a skos:Concept ; skos:topConceptOf ex:scheme ; skos:prefLabel \"Tea\"@en .\r\n"
        + "ex:milk a skos:Concept ; skos:inScheme ex:scheme ; skos:prefLabel \"Milk\"@en .\r\n";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly RdfContextActionProvider _actions;
    private readonly RdfContextPropertyProvider _properties;
    private readonly IHistoryStack _history;

    public SkosActionsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddRdf()
            .BuildServiceProvider();
        _actions = new RdfContextActionProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _properties = new RdfContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string WriteBody(string content, string name = "scheme.ttl")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    private const string Tea = "res:http://example.org/tea";
    private const string Milk = "res:http://example.org/milk";

    [Fact]
    public async Task ARelationGestureBetweenConcepts_LeadsWithTheThesaurusGestures()
    {
        // Arrange.
        var body = WriteBody(Vocabulary);
        var gesture = RdfRelationGesture.IdFor(Milk, Tea);

        // Act.
        var groups = await _actions.DiscoverAsync(Target(body, gesture), TestContext.Current.CancellationToken);
        var ids = groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();

        // Assert: skos first, the family's generic predicate dialog still beneath.
        Assert.Equal(SkosActions.FileUnderActionId, ids[0]);
        Assert.Contains(SkosActions.RelateActionId, ids);
        Assert.Contains(RdfContextActionProvider.ConnectActionId, ids);
    }

    [Fact]
    public async Task FileUnder_WritesOneBroaderOnTheDraggedEnd_AndUndoRestoresBytes()
    {
        // Arrange: drag from milk, release on tea - milk goes under tea.
        var body = WriteBody(Vocabulary);
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(body, RdfRelationGesture.IdFor(Milk, Tea)), SkosActions.FileUnderActionId, TestContext.Current.CancellationToken);

        // Assert: one skos:broader on the narrower end, no inverse co-written (Requirement 5.1).
        Assert.IsType<ContextExecutionCompleted>(result);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("ex:milk", text, StringComparison.Ordinal);
        Assert.Contains("skos:broader ex:tea", text, StringComparison.Ordinal);
        Assert.DoesNotContain("skos:narrower", text, StringComparison.Ordinal);

        // Act & assert, continued: one undo, bytes identical.
        Assert.True((await _history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FileUnder_OnAnLfDocument_KeepsItsLineEndings_AndUndoesByteForByte()
    {
        // Arrange: an LF vocabulary, which is what a .ttl authored anywhere but Windows is -
        // and what every other test in this file is NOT, since they all seed CRLF. A writer
        // that appended its own house ending here would leave the user's file mixed, and the
        // family's Requirement 1.4 says only the lines an edit concerns may change.
        var body = WriteBody(Vocabulary.Replace("\r\n", "\n", StringComparison.Ordinal));
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\r\n", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(body, RdfRelationGesture.IdFor(Milk, Tea)), SkosActions.FileUnderActionId, TestContext.Current.CancellationToken);

        // Assert: the statement landed, and the file is still LF throughout - no CR anywhere.
        Assert.IsType<ContextExecutionCompleted>(result);
        var after = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("skos:broader ex:tea", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", after, StringComparison.Ordinal);

        // Assert, continued: and the undo returns the original bytes, endings included.
        Assert.True((await _history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disconnect_TakesBothAssertedDirections_AsOneUndo()
    {
        // Arrange: the pair asserted both ways; the drawn edge is one, its id canonical.
        var body = WriteBody(Vocabulary
            + "ex:milk skos:broader ex:tea .\r\n"
            + "ex:tea skos:narrower ex:milk .\r\n");
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var edgeId = $"edge:{Milk}|{SkosVocabulary.Broader}|{Tea}";

        // Act: discovery names both directions; execute asks; the confirmed leg commits.
        var groups = await _actions.DiscoverAsync(Target(body, edgeId), TestContext.Current.CancellationToken);
        Assert.Contains("both directions", groups.SelectMany(g => g.Actions).Single().Label, StringComparison.Ordinal);
        var asked = await _actions.ExecuteAsync(Target(body, edgeId), SkosActions.DisconnectActionId, TestContext.Current.CancellationToken);
        Assert.IsType<ContextExecutionRequiresConfirmation>(asked);
        var committed = await _actions.CommitAsync(Target(body, edgeId), SkosActions.DisconnectActionId, "", "", TestContext.Current.CancellationToken);

        // Assert: both statements gone as one command, one undo restoring both.
        Assert.True(committed.Completed, committed.Error);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("skos:broader ex:tea", text, StringComparison.Ordinal);
        Assert.DoesNotContain("skos:narrower ex:milk", text, StringComparison.Ordinal);
        Assert.True((await _history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddConcept_IsThreeTriplesOneCommand_CollisionRefusedFirst()
    {
        // Arrange.
        var body = WriteBody(Vocabulary);
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var placement = RdfNewPlacement.IdFor(120, 80);

        // Act & assert: offered where the file asserts a scheme.
        var groups = await _actions.DiscoverAsync(Target(body, placement), TestContext.Current.CancellationToken);
        Assert.Contains(SkosActions.AddConceptActionId, groups.SelectMany(g => g.Actions).Select(a => a.Id));

        // A colliding label is refused before any splice (the minted IRI already names ex:tea).
        var collision = await _actions.ValidateAsync(Target(body, placement), SkosActions.AddConceptActionId, "tea", TestContext.Current.CancellationToken);
        Assert.False(collision.Valid);

        // The commit states type, label and scheme as one command.
        var committed = await _actions.CommitAsync(Target(body, placement), SkosActions.AddConceptActionId, "Green tea", "", TestContext.Current.CancellationToken);
        Assert.True(committed.Completed, committed.Error);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("ex:Green_tea", text, StringComparison.Ordinal);
        Assert.Contains("\"Green tea\"@en", text, StringComparison.Ordinal);
        Assert.Contains("skos:inScheme ex:scheme", text, StringComparison.Ordinal);

        // One undo removes all three.
        Assert.True((await _history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheConceptGrid_ListsLabelsByLanguage_AndAPrefLabelEditKeepsItsTag()
    {
        // Arrange: two preferred labels, documentation, and an out-of-file mapping.
        var body = WriteBody(Vocabulary.Replace(
            "ex:tea a skos:Concept ; skos:topConceptOf ex:scheme ; skos:prefLabel \"Tea\"@en .",
            "ex:tea a skos:Concept ; skos:topConceptOf ex:scheme ; skos:prefLabel \"Tea\"@en ; skos:prefLabel \"Thee\"@nl ;"
            + " skos:definition \"An infusion.\"@en ; skos:exactMatch <http://elsewhere.org/tea> .",
            StringComparison.Ordinal));

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, Tea), TestContext.Current.CancellationToken);

        // Assert: the skos grid, not the family's.
        Assert.Contains(rows, row => row.Id == "skos.label:prefLabel:en" && row.Value == "Tea" && row.ReadOnlyReason.Length == 0);
        Assert.Contains(rows, row => row.Id == "skos.label:prefLabel:nl" && row.Value == "Thee");
        Assert.Contains(rows, row => row.Id == "skos.doc:definition" && row.Value == "An infusion.");
        Assert.Contains(rows, row => row.Id == "skos.schemes" && row.ReadOnlyReason.Length > 0);
        Assert.Contains(rows, row => row.Id.StartsWith("skos.mapping:", StringComparison.Ordinal) && row.ReadOnlyReason.Length > 0);

        // Act, continued: edit the Dutch label; only that literal's lexical token changes.
        var set = await _properties.SetAsync(Target(body, Tea), "skos.label:prefLabel:nl", "Zwarte thee", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(set.IsSuccess, set.Error);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("\"Zwarte thee\"@nl", text, StringComparison.Ordinal);
        Assert.Contains("\"Tea\"@en", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnXlLabeledConcept_RefusesLabelEdits_WithTheBoundarySentence()
    {
        // Arrange.
        var body = WriteBody(Vocabulary
            + "@prefix skosxl: <http://www.w3.org/2008/05/skos-xl#> .\r\n"
            + "ex:tea skosxl:prefLabel ex:teaLabel .\r\n");

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, Tea), TestContext.Current.CancellationToken);
        var set = await _properties.SetAsync(Target(body, Tea), "skos.label:prefLabel:en", "X", TestContext.Current.CancellationToken);

        // Assert: read-only with the Requirement 3.6 sentence, and the edit refused.
        Assert.Contains(rows, row => row.Id == "skos.label:prefLabel:en" && row.ReadOnlyReason.Contains("SKOS-XL", StringComparison.Ordinal));
        Assert.False(set.IsSuccess);
    }

    [Fact]
    public void TheToolbox_OffersAConcept_UnderTheSkosOrigin()
    {
        // Arrange & act.
        var toolbox = _provider.GetServices<IDiagramToolboxProvider>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSkosExtension.SkosOrigin);

        // Assert: one entry, naming the add action its drop commits.
        var item = Assert.Single(toolbox.Items);
        Assert.Equal(SkosActions.AddConceptActionId, item.DropActionId);
    }
}
