using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
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
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("structurizr");

    /// <summary>
    /// Reads every file through the vendored binding and through the module. The binding reads all eight kinds under the
    /// module's type names, and a relationship as an element of its own whose ends are its <c>from</c> and <c>to</c>
    /// (FBL 0.11); the workspace, a view's statements and a dynamic view's interactions are no element of the module.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("structurizr", file, IdOf, Module);

    private static string? IdOf(FblElement element) => element.Type switch
    {
        "Relationship" => $"{element.Attributes.GetValueOrDefault("from")}->{element.Attributes.GetValueOrDefault("to")}",
        "Workspace" or "ViewInclude" or "Interaction" => null,
        _ => element.IdIsStored ? element.Id : null,
    };

    /// <summary>The ids C4Parser makes for an element without an identifier: kind, name slug, counter.</summary>
    [GeneratedRegex("^(person|softwaresystem|container|component|deploymentnode|infrastructurenode|containerinstance|softwaresysteminstance)_[A-Za-z0-9]+_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedId();

    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var workspace = C4Parser.Parse(LineDocument.Parse(text));
        foreach (var element in workspace.Elements)
        {
            if (!GeneratedId().IsMatch(element.Id)) ModuleCrossCheck.Add(ids, element.Kind.ToString(), element.Id);
        }
        foreach (var relationship in workspace.Relationships) ModuleCrossCheck.Add(ids, "Relationship", $"{relationship.SourceId}->{relationship.DestinationId}");
        return ids;
    }
}
