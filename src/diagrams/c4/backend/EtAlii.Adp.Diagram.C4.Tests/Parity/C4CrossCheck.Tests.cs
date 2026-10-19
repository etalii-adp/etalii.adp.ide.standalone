using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for C4: the ids per type the vendored Structurizr
/// binding reads from every <c>.dsl</c> file of the repository are the ones <see cref="C4Parser"/> reads, a relationship
/// by its ends. It moved here from <c>EtAlii.Adp.Specification.Fbl.Tests</c> so that no core project references a tool
/// module (code-structure T287); its divergences are still listed in that project's <c>RealFiles/divergences.json</c>.
/// </summary>
public partial class C4CrossCheckTests
{
    /// <summary>The C4 kinds the binding has rules for; the others (components, deployment) it does not read.</summary>
    private static readonly IReadOnlyDictionary<C4ElementKind, string> _types = new Dictionary<C4ElementKind, string>
    {
        [C4ElementKind.Person] = "Person",
        [C4ElementKind.SoftwareSystem] = "SoftwareSystem",
        [C4ElementKind.Container] = "Container",
    };

    public static TheoryData<string> Files() => ModuleCrossCheck.Files("structurizr");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("structurizr", file, element => element.IsRelation ? $"{element.Source}->{element.Target}" : element.IdIsStored ? element.Id : null, Module);

    /// <summary>The ids C4Parser makes for an element without an identifier: kind, name slug, counter.</summary>
    [GeneratedRegex("^(person|softwaresystem|container)_[A-Za-z0-9]+_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedId();

    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var workspace = C4Parser.Parse(LineDocument.Parse(text));
        foreach (var element in workspace.Elements)
        {
            if (_types.TryGetValue(element.Kind, out var type) && !GeneratedId().IsMatch(element.Id)) ModuleCrossCheck.Add(ids, type, element.Id);
        }
        foreach (var relationship in workspace.Relationships) ModuleCrossCheck.Add(ids, "Relationship", $"{relationship.SourceId}->{relationship.DestinationId}");
        return ids;
    }
}
