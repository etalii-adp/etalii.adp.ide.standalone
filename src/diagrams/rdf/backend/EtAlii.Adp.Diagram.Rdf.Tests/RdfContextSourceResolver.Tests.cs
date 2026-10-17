using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// A selection in a data graph, by the contract every element resolver keeps. The element followed is
/// a resource, and what it shows is its prefixed name, so a rename is a change of the prefix its
/// namespace is written with: the IRI, and so the id, stays.
/// </summary>
public sealed class RdfContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly RdfDocumentStore _store = new();

    /// <summary>The prefix the example's namespace is written with now.</summary>
    private string _prefix = "ex";

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "constructs.ttl");

    protected override IContextSourceResolver CreateResolver() =>
        new RdfContextSourceResolver(
            ServiceCollectionAddRdfExtension.RdfOrigin,
            new DiagramFileRouter(new FamilyCatalog()),
            _store,
            new RdfReading(new RdfElementMapper()).Drawn);

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "res:http://example.org/alice";

    protected override IReadOnlyList<string> ElementPath => ["ex:alice"];

    protected override string ElementType => RdfElementMapper.ResourceType;

    protected override string Gesture => "rel:res:http://example.org/alice->res:http://example.org/bob";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an RDF document.",
        "That element is no longer in this file.",
        "New element",
        "New element");

    /// <summary>Writes the example's namespace with a prefix made of <paramref name="name"/>'s letters, which is what a resource shows.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var prefix = new string([.. name.Where(char.IsAsciiLetter)]).ToLowerInvariant();
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, $@"\b{_prefix}:", $"{prefix}:", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        _prefix = prefix;
        return [$"{prefix}:alice"];
    }

    private sealed class FamilyCatalog : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
    }
}
