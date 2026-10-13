using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>A selection on the bug fixer example, by the contract every element resolver keeps, and what is its own.</summary>
public sealed class AbmContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly AbmDocumentStore _store = new();

    protected override string Example => AbmExamples.BodyOf("bug-fixer");

    protected override IContextSourceResolver CreateResolver() =>
        new AbmContextSourceResolver(new DiagramFileRouter(new AbmOnlyCatalog()), _store, new AbmElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "1.1";

    protected override IReadOnlyList<string> ElementPath => ["Try in order: Understand the report"];

    protected override string ElementType => AbmElementMapper.FallbackType;

    protected override string Gesture => "rel:1->1.2";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a behavior model.",
        "That node is no longer in this behavior model.",
        "New node",
        "New parent line");

    /// <summary>A behavior model shares <c>.md</c> with every Markdown file, so it is routed only through the registration beside it.</summary>
    protected override string SelectedIn(string body)
    {
        var registration = Path.ChangeExtension(body, ".adp");
        if (!File.Exists(registration))
        {
            File.Copy(Path.ChangeExtension(Example, ".adp"), registration);
        }

        return registration;
    }

    /// <summary>A node's label is the text after its keyword on the list item, not a <c>name:</c> key.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, @"(\*\*Try in order:\*\* )Understand the report|(\*\*Try in order:\*\* )Renamed once", match => match.Groups[1].Value + match.Groups[2].Value + name, RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return ["Try in order: " + name];
    }

    [Fact]
    public async Task ANodeWithChildren_SaysItHasThem_AndALeafThatItHasNone()
    {
        // Act.
        var parent = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("1.1"));
        var leaf = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("1.1.1"));

        // Assert.
        Assert.True(parent.Level.Detail.Element.HasChildren);
        Assert.False(leaf.Level.Detail.Element.HasChildren);
    }
}

internal sealed class AbmOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
