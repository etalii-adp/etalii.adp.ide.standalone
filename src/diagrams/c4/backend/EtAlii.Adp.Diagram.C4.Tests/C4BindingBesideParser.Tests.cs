using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using EtAlii.Adp.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The module's embedded binding read beside <see cref="C4Parser"/> (c4-disl-fbl task 13, Requirements 10.4 and 11.2):
/// every <c>.dsl</c> of the repository read through <c>structurizr.fbl</c> and its grammar, as the module embeds them,
/// and through the parser, and what still differs listed in <c>divergences.json</c> with why. The elements and
/// relationships are the cross-check's (<see cref="Parity.C4CrossCheckTests"/>), which reads the vendored copy of the
/// same bytes; this test holds the two copies equal and compares the parents and the views. The binding is on no
/// reading path: the parser reads every body.
/// </summary>
public partial class C4BindingBesideParserTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    public static TheoryData<string> Files() => ModuleCrossCheck.Files("structurizr");

    [Theory]
    [InlineData("structurizr.fbl")]
    [InlineData("structurizr.lark")]
    public void TheEmbeddedBindingIsTheVendoredExample(string file)
    {
        // Arrange.
        var vendored = File.ReadAllBytes(RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance", file));

        // Act.
        var embedded = _module.ResourceBytes(file);

        // Assert: one binding, so the cross-check of the vendored copy is the cross-check of the module's (the .fbl
        // compared without its line endings, which git gives a working tree as the house style says).
        Assert.Equal(Normalised(vendored), Normalised(embedded));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheParentsAndViewsTheParserReads(string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = BundledBinding.Load(_module, "structurizr.fbl", "workspace");

        // Act.
        var model = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file)).Model;
        var workspace = C4Parser.Parse(LineDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes)));

        // Assert.
        Divergences.Check("beside-parser", "structurizr", file, model.Unreadable ? "unreadable" : ModuleCrossCheck.Difference(Read(model), Parsed(workspace)));
    }

    /// <summary>
    /// What the binding reads: each element with a stored id in its parent, when that has one too, the name of each view,
    /// and the path of each <c>!include</c>.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> Read(FblModel model)
    {
        var read = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var stored = model.Elements.Where(element => element.IdIsStored).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var element in model.Elements.Where(element => element.IdIsStored && element.ParentId is { } parent && stored.Contains(parent))) ModuleCrossCheck.Add(read, "Parent", $"{element.Id} in {element.ParentId}");
        foreach (var view in model.ViewNames) ModuleCrossCheck.Add(read, "View", view);
        foreach (var include in model.Elements.Where(element => element.Type == "IncludedFile")) ModuleCrossCheck.Add(read, "Include", include.Attributes.GetValueOrDefault("path") as string ?? "");
        return read;
    }

    /// <summary>
    /// What the parser reads: each element in its parent, both named by an identifier rather than by the ids the parser
    /// makes for the others, the key of each view, and each file it does not follow.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> Parsed(C4Workspace workspace)
    {
        var parsed = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in workspace.Elements.Where(element => element.ParentId is { } parent && !GeneratedId().IsMatch(element.Id) && !GeneratedId().IsMatch(parent))) ModuleCrossCheck.Add(parsed, "Parent", $"{element.Id} in {element.ParentId}");
        foreach (var view in workspace.Views) ModuleCrossCheck.Add(parsed, "View", view.Key);
        foreach (var include in workspace.Includes) ModuleCrossCheck.Add(parsed, "Include", include);
        return parsed;
    }

    /// <summary>The ids C4Parser makes for an element without an identifier: kind, name slug, counter.</summary>
    [GeneratedRegex("^(person|softwaresystem|container|component|deploymentnode|infrastructurenode|containerinstance|softwaresysteminstance)_[A-Za-z0-9]+_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedId();

    private static byte[] Normalised(byte[] bytes) => [.. bytes.Where(b => b != (byte)'\r')];
}
