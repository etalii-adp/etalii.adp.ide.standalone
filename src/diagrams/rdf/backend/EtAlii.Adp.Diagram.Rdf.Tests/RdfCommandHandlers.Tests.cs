using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// What every handler of the family does with a save, through the shared contract and for each
/// command of each reading - RDF, SKOS and SHACL alike run on the one handler base (code-structure
/// FR-022, FR-033).
/// </summary>
public sealed class RdfCommandHandlersTests : EditHandlerContract<IRdfDocumentStore, LineDocument, RdfModel>
{
    private const string SaveWarning = "The handlers' test warning: something beside the file was not kept.";

    private const string Ex = "http://example.org/";

    /// <summary>A file every reading's commands find something in: plain triples, a shape and a small vocabulary.</summary>
    private const string Corpus =
        "@prefix ex: <http://example.org/> .\r\n"
        + "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n"
        + "@prefix skos: <http://www.w3.org/2004/02/skos/core#> .\r\n"
        + "\r\n"
        + "ex:alice ex:knows ex:bob ;\r\n"
        + "    ex:name \"Alice\" . # who else\r\n"
        + "\r\n"
        + "ex:PersonShape a sh:NodeShape ;\r\n"
        + "    sh:targetClass ex:Person ;\r\n"
        + "    sh:name \"Person\" .\r\n"
        + "\r\n"
        + "ex:animals a skos:ConceptScheme .\r\n"
        + "ex:mammal a skos:Concept .\r\n"
        + "ex:cat a skos:Concept ;\r\n"
        + "    skos:broader ex:mammal .\r\n";

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "constructs.ttl");

    protected override IRdfDocumentStore CreateStore() => new RdfDocumentStore(new FileWriter());

    protected override IRdfDocumentStore Warning(IRdfDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IRdfDocumentStore store) =>
        new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .AddSingleton(store)
            .BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

    protected override ICommand EditOf(string bodyPath) => new RenameRdfTermCommand(bodyPath, Ex + "alice", Ex + "alicia");

    protected override ICommand EditOfMissing(string bodyPath) =>
        new RemoveRdfTripleCommand(bodyPath, Ex + "nobody", Ex + "knows", Ex + "alice");

    /// <summary>Every command of the family, by name.</summary>
    public static TheoryData<string> Commands() =>
    [
        "add-triple", "remove-triple", "remove-resource", "rename", "replace-literal", "add-prefix",
        "add-concept", "disconnect",
        "add-property-row", "add-target", "remove-target", "create-shape", "deactivate", "set-literal", "remove-shape",
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task EveryHandler_CarriesTheSaveWarning_AndCanBeUndone(string edit)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = TestFolder.Create("EtAlii.Adp.RdfCommandHandlersTests");
        var body = folder.Combine("family.ttl");
        await File.WriteAllTextAsync(body, Corpus, cancellationToken);
        var dispatcher = Dispatcher(Warning(CreateStore(), SaveWarning));

        // Act.
        var result = await dispatcher.DispatchAsync(CommandOf(edit, body), cancellationToken);
        var edited = await File.ReadAllTextAsync(body, cancellationToken);
        var undone = await dispatcher.DispatchAsync(result.Inverse!, cancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SaveWarning, result.Warning);
        Assert.NotEqual(Corpus, edited);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(body, cancellationToken));
    }

    [Fact]
    public async Task AnEditOfAFileThatDoesNotParse_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = TestFolder.Create("EtAlii.Adp.RdfCommandHandlersTests");
        var body = folder.Combine("broken.ttl");
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "broken.ttl"), body);
        var before = await File.ReadAllBytesAsync(body, cancellationToken);

        // Act.
        var result = await Dispatcher(CreateStore()).DispatchAsync(
            new AddRdfPrefixCommand(body, "foaf", "http://xmlns.com/foaf/0.1/"), cancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("could not be read", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, cancellationToken));
    }

    private static ICommand CommandOf(string edit, string body) => edit switch
    {
        "add-triple" => new AddRdfTripleCommand(body, Ex + "alice", Ex + "knows", Ex + "carol"),
        "remove-triple" => new RemoveRdfTripleCommand(body, Ex + "alice", Ex + "knows", Ex + "bob"),
        "remove-resource" => new RemoveRdfResourceCommand(body, Ex + "bob"),
        "rename" => new RenameRdfTermCommand(body, Ex + "bob", Ex + "robert"),
        "replace-literal" => new ReplaceRdfObjectLiteralCommand(body, Ex + "alice", Ex + "name", "Alice", "", "", "Alicia"),
        "add-prefix" => new AddRdfPrefixCommand(body, "foaf", "http://xmlns.com/foaf/0.1/"),
        "add-concept" => new AddSkosConceptCommand(body, Ex + "dog", "Dog", "en", Ex + "animals"),
        "disconnect" => new DisconnectSkosPairCommand(body, Ex + "cat", Ex + "mammal", Hierarchy: true),
        "add-property-row" => new AddShaclPropertyRowCommand(body, Ex + "PersonShape", Ex + "age", MinCount: 0, MaxCount: 1),
        "add-target" => new AddShaclTargetCommand(body, Ex + "PersonShape", ShaclVocabulary.TargetNode, Ex + "alice"),
        "remove-target" => new RemoveShaclTargetCommand(body, Ex + "PersonShape", ShaclVocabulary.TargetClass, Ex + "Person"),
        "create-shape" => new CreateShaclNodeShapeCommand(body, Ex + "AddressShape"),
        "deactivate" => new SetShaclDeactivatedCommand(body, Ex + "PersonShape", Deactivated: true),
        "set-literal" => new SetShaclLiteralCommand(body, Ex + "PersonShape", ShaclVocabulary.Name, "Human"),
        "remove-shape" => new RemoveShaclShapeCommand(body, Ex + "PersonShape"),
        _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit."),
    };

    private sealed class WarningStore(IRdfDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, RdfModel>(inner, warning), IRdfDocumentStore;
}
