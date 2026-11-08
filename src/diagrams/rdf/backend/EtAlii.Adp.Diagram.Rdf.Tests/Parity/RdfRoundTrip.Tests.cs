using System.Text;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.History;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The body read through the plugin is written as it was (rdf-disl-fbl task 7, Requirements 5.2, 5.4 and 5.6): every
/// document of the corpus saved without a change gives the same bytes, a byte-order mark, the line-ending fixtures and
/// a file without a final newline among them; every edit, whatever number of triples it adds or removes, is one undo
/// step that gives the bytes back; and every refusal is today's sentence and leaves the body untouched to the byte.
/// </summary>
public class RdfRoundTripTests
{
    private const string Ex = "http://example.org/";

    /// <summary>A small SHACL file with one IRI-named shape and its target, for the five refusals of <see cref="ShaclRefusals"/>.</summary>
    private const string Shapes = "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n@prefix ex: <http://example.org/> .\r\n\r\nex:PersonShape a sh:NodeShape ;\r\n    sh:targetClass ex:Person ;\r\n    sh:property [ sh:path ex:name ; sh:minCount 1 ] .\r\n";

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var body in RdfCorpus.Bodies())
        {
            data.Add(body);
        }

        return data;
    }

    /// <summary>Each edit, by name, on the fixture it is made on.</summary>
    public static TheoryData<string, string> Edits() => new()
    {
        { "shared-statement.ttl", "add a pair to a statement" },
        { "shared-statement.ttl", "remove the most named object with every triple naming it" },
        { "shared-statement.ttl", "rename a subject wherever it is written" },
        { "shared-statement.ttl", "remove a pair of a ',' list" },
        { "shared-statement.ttl", "replace a literal" },
        { "shared-statement.ttl", "declare a prefix" },
        { "byte-order-mark.ttl", "add a pair to a statement" },
        { "byte-order-mark.ttl", "rename a subject wherever it is written" },
        { "crlf-line-endings.ttl", "add a pair to a statement" },
        { "crlf-line-endings.ttl", "remove the most named object with every triple naming it" },
        { "lf-line-endings.ttl", "add a pair to a statement" },
        { "lf-line-endings.ttl", "remove the most named object with every triple naming it" },
        { "tied-line-endings.ttl", "add a pair to a statement" },
        { "no-trailing-newline.ttl", "add a pair to a statement" },
        { "no-trailing-newline.ttl", "remove the most named object with every triple naming it" },
        { "simple.nt", "add a pair to a statement" },
        { "simple.nt", "remove the most named object with every triple naming it" },
        { "shapes", "add a property row of four triples" },
        { "shapes", "remove a shape with its blank subtrees" },
        { "shapes", "create a node shape" },
        { "skos-merged-hierarchy.ttl", "add a concept to its scheme" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDocument_SavedWithoutAChange_IsTheSameBytes(string relative)
    {
        // Arrange.
        await using var host = new RdfHost();
        var path = host.Copy(relative);
        var original = RdfHost.Bytes(path);
        var entry = await host.Store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var saved = await host.Store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert: a body that does not parse is refused and not written; any other is written as it was.
        Assert.Equal(!entry.IsUsable, saved.Failed);
        Assert.Equal(original, RdfHost.Bytes(path));
        Assert.Equal(original, RdfBody.Open(original, path).Bytes);
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public async Task EveryEdit_IsOneUndoStep_ThatGivesTheBytesBack(string fixture, string edit)
    {
        // Arrange.
        await using var host = new RdfHost();
        var path = fixture == "shapes" ? host.Write("shapes.ttl", Encoding.UTF8.GetBytes(Shapes)) : host.Copy(Fixture(fixture));
        var original = RdfHost.Bytes(path);
        var model = (await host.Store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model;

        // Act.
        var done = await host.History.ExecuteAsync(CommandFor(edit, path, model), TestContext.Current.CancellationToken);
        var edited = RdfHost.Bytes(path);
        var undone = await host.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(done.IsSuccess, done.Error);
        Assert.NotEqual(original, edited);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(original, RdfHost.Bytes(path));
        Assert.False(host.History.CanUndo);
        Assert.Equal(Encoding.UTF8.GetString(original).TrimStart('﻿'), host.Store.Get(path)!.Document.Text);
    }

    [Fact]
    public async Task EveryRefusedEdit_IsTodaysSentence_AndLeavesTheBodyUntouched()
    {
        // Arrange.
        await using var host = new RdfHost();
        var broken = host.Copy(Fixture("broken.ttl"));
        var blank = host.Write("blank.ttl", "@prefix ex: <http://example.org/> .\r\n\r\nex:a ex:p [ ex:q ex:r ] .\r\n"u8.ToArray());
        var shapes = host.Write("shapes.ttl", Encoding.UTF8.GetBytes(Shapes));
        (string Path, ICommand Command, string Sentence)[] refusals =
        [
            (broken, new AddRdfPrefixCommand(broken, "parity", Ex + "parity#"), "broken.ttl could not be read, so nothing can be edited until it can: it does not parse at line 2. Expected an object, found ''."),
            (blank, new RemoveRdfResourceCommand(blank, Ex + "a"), "That element is rooted in a blank node, which has no identity that survives a reparse, so this edit cannot land in the file. Name the node with an IRI to edit it."),
            (shapes, new RemoveShaclShapeCommand(shapes, ""), "This constraint is written as a blank node, which has no identity that survives a reparse, so an edit keyed to it could not be undone reliably. Open the file as text to change it, or give the shape an IRI of its own."),
            (shapes, new RemoveShaclShapeCommand(shapes, Ex + "Missing"), "No IRI-named shape of that name is in this file, so there is nothing to edit."),
            (shapes, new AddShaclPropertyRowCommand(shapes, Ex + "PersonShape", ""), "A property shape needs exactly one sh:path, so a path is required before the row can be written."),
            (shapes, new RemoveShaclTargetCommand(shapes, Ex + "PersonShape", "http://www.w3.org/ns/shacl#targetClass", Ex + "Missing"), "That target is not declared in this file, so there is nothing to remove."),
            (shapes, new RemoveShaclTargetCommand(shapes, Ex + "PersonShape", "", Ex + "PersonShape"), "This shape targets its own instances because the file states it to be a class, not through a target triple, so there is no declaration to remove. Remove the class typing instead, as text."),
        ];

        foreach ((string path, ICommand command, string sentence) in refusals)
        {
            var before = RdfHost.Bytes(path);
            var held = (await host.Store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document;

            // Act.
            var result = await host.History.ExecuteAsync(command, TestContext.Current.CancellationToken);

            // Assert.
            Assert.False(result.IsSuccess, $"{command} was not refused.");
            Assert.Equal(sentence, result.Error);
            Assert.Equal(before, RdfHost.Bytes(path));
            Assert.Same(held, host.Store.Get(path)!.Document);
        }

        Assert.False(host.History.CanUndo);
    }

    [Fact]
    public async Task EveryRefusedMove_IsTodaysSentence_AndLeavesTheBodyAndTheRegistrationUntouched()
    {
        // Arrange: a registered graph with a blank node, a registered ontology with a class expression, and a bare file.
        await using var host = new RdfHost();
        var graph = host.Write("blank.ttl", "@prefix ex: <http://example.org/> .\r\n\r\nex:a ex:p [ ex:q ex:r ] .\r\n"u8.ToArray());
        var graphRegistration = host.Write("blank.adp", "w3c/rdf\r\nbody: blank.ttl\r\n"u8.ToArray());
        var ontology = host.Copy(Fixture("owl-ontology.ttl"));
        var ontologyRegistration = host.Write("owl-ontology.adp", "w3c/owl\r\nbody: owl-ontology.ttl\r\n"u8.ToArray());
        var bare = host.Copy(Fixture("unregistered.ttl"));
        var rdf = host.Open(ServiceCollectionAddRdfExtension.RdfOrigin, graph, graphRegistration);
        var owl = host.Open(ServiceCollectionAddRdfExtension.OwlOrigin, ontology, ontologyRegistration);
        var unregistered = host.Open(ServiceCollectionAddRdfExtension.RdfOrigin, bare);
        var blankId = (await RdfHost.DrawnAsync(rdf, TestContext.Current.CancellationToken)).First(element => element.Id.StartsWith("blank:", StringComparison.Ordinal)).Id;
        var expressionId = (await RdfHost.DrawnAsync(owl, TestContext.Current.CancellationToken)).First(element => element.Id.StartsWith("expr:", StringComparison.Ordinal)).Id;
        var resourceId = (await RdfHost.DrawnAsync(unregistered, TestContext.Current.CancellationToken)).First(element => element.Id.StartsWith("res:", StringComparison.Ordinal)).Id;
        (Sessions.IDiagramSession Session, string Id, string[] Files, string Sentence)[] moves =
        [
            (rdf, blankId, [graph, graphRegistration], "That is a blank node, whose identity does not survive a reparse, so a stored position could not be trusted. Name it with an IRI to arrange it."),
            (owl, expressionId, [ontology, ontologyRegistration], "That is an anonymous class expression, whose identity does not survive an edit to the file, so a stored position could not be trusted. It takes its place beside the class that uses it; edit the expression as text."),
            (unregistered, resourceId, [bare], "This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it."),
        ];

        foreach ((Sessions.IDiagramSession session, string id, string[] files, string sentence) in moves)
        {
            var before = files.Select(RdfHost.Bytes).ToList();

            // Act.
            var refusal = await session.MoveElementToAsync(id, 400, 300, TestContext.Current.CancellationToken);

            // Assert.
            Assert.Equal(sentence, refusal);
            Assert.Equal(before, files.Select(RdfHost.Bytes));
        }

        Assert.False(host.History.CanUndo);
    }

    private static string Fixture(string name) => Path.Combine("diagrams", "rdf", "backend", "EtAlii.Adp.Diagram.Rdf.Tests", "Fixtures", name);

    /// <summary>The command <paramref name="edit"/> names, built from what the document states.</summary>
    private static ICommand CommandFor(string edit, string path, RdfModel model) => edit switch
    {
        "add a pair to a statement" => new AddRdfTripleCommand(path, FirstSubject(model), Ex + "parity", "", "Parity", "en"),
        "remove the most named object with every triple naming it" => new RemoveRdfResourceCommand(path, MostNamed(model)),
        "rename a subject wherever it is written" => new RenameRdfTermCommand(path, FirstSubject(model), FirstSubject(model) + "Renamed"),
        "remove a pair of a ',' list" => RemoveListed(path, model),
        "replace a literal" => ReplaceLiteral(path, model),
        "declare a prefix" => new AddRdfPrefixCommand(path, "parity", Ex + "parity#"),
        "add a property row of four triples" => new AddShaclPropertyRowCommand(path, Ex + "PersonShape", Ex + "age", "http://www.w3.org/2001/XMLSchema#integer", 0, 1, "Age"),
        "remove a shape with its blank subtrees" => new RemoveShaclShapeCommand(path, Ex + "PersonShape"),
        "create a node shape" => new CreateShaclNodeShapeCommand(path, Ex + "OrderShape"),
        "add a concept to its scheme" => new AddSkosConceptCommand(path, Ex + "parity", "Parity", "en", Scheme(model)),
        _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit."),
    };

    private static string FirstSubject(RdfModel model) => model.Triples.Select(triple => triple.Subject).OfType<IriTerm>().First().Iri;

    /// <summary>The IRI most triples name as their object.</summary>
    private static string MostNamed(RdfModel model) =>
        model.Triples.Select(triple => triple.Object).OfType<IriTerm>().GroupBy(term => term.Iri, StringComparer.Ordinal).OrderByDescending(group => group.Count()).First().Key;

    private static RemoveRdfTripleCommand RemoveListed(string path, RdfModel model)
    {
        var listed = model.Triples.First(triple => triple is { IsListContinuation: true, Subject: IriTerm, Object: IriTerm });
        return new RemoveRdfTripleCommand(path, ((IriTerm)listed.Subject).Iri, listed.Predicate.Iri, ((IriTerm)listed.Object).Iri);
    }

    private static ReplaceRdfObjectLiteralCommand ReplaceLiteral(string path, RdfModel model)
    {
        var triple = model.Triples.First(candidate => candidate is { Subject: IriTerm, Object: LiteralTerm });
        var literal = (LiteralTerm)triple.Object;
        return new ReplaceRdfObjectLiteralCommand(path, ((IriTerm)triple.Subject).Iri, triple.Predicate.Iri, literal.Lexical, literal.Language ?? "", literal.DatatypeIri ?? "", "Replaced");
    }

    private static string Scheme(RdfModel model) =>
        ((IriTerm)model.Triples.First(triple => triple.Object is IriTerm { Iri: "http://www.w3.org/2004/02/skos/core#ConceptScheme" }).Subject).Iri;
}
