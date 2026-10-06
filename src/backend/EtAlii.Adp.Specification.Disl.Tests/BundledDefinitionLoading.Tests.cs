using System.Text.Json.Nodes;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Both bundled definitions load with this runtime: nothing refused and nothing warned about, every
/// expression compiled in its own context. A function either calls that the runtime lacks fails the
/// load by name, wherever the call is.
/// </summary>
public class BundledDefinitionLoadingTests
{
    [Theory]
    [InlineData(Resources.HypeCycle, "net.etalii.adp.gartner.hypecycle-graph", 250)]
    [InlineData(Resources.BehaviorModel, "net.etalii.adp.etalii.agent-behavior-modelling", 80)]
    public void TheBundledDefinition_LoadsWithoutADiagnostic(string resource, string language, int atLeast)
    {
        // Act.
        var bundled = BundledDefinition.Load(Resources.Assembly, resource);

        // Assert.
        Assert.Empty(bundled.Diagnostics);
        Assert.Equal(language, bundled.Specification.LanguageId);
        Assert.Equal("0.3", bundled.Specification.Disl);
        Assert.True(bundled.Specification.Expressions.Count + bundled.Specification.Functions.Count >= atLeast, $"Only {bundled.Specification.Expressions.Count} expressions were compiled; the walk has lost some.");
    }

    /// <summary>
    /// Both bundled definitions are DISL 0.3 and say nothing through the <c>x-</c> keys DISL 0.3 adopted
    /// (2026-10-05): each is renamed to its standard key, which is the only one the runtime reads. The two
    /// <c>x-</c> keys that still hold a construct not yet ruled on hold that construct and nothing else.
    /// </summary>
    [Theory]
    [InlineData(Resources.HypeCycle)]
    [InlineData(Resources.BehaviorModel)]
    public void TheBundledDefinition_IsDisl03WithoutTheRenamedExtensionKeys(string resource)
    {
        // Arrange.
        var renamed = new HashSet<string>(StringComparer.Ordinal)
        {
            "x-bounds.neighbour", "x-enumValue.colorToken", "x-ruler.minUnit", "x-part.tooltip", "x-label.editText", "x-anchors.drawnFrom",
            "x-field.display", "x-field.parse", "x-builtIn.code", "x-layout.rowPacked", "x-persistence.typeMap",
            "x-abm-connectGesture", "x-abm-retype", "x-abm-place", "x-abm-placements", "x-abm-tidyTree",
        };
        var keptFor = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["x-layout.rowArrange"] = ["pinRowsOfTallNotes", "doc"],
            ["x-abm-plan"] = ["retype", "doc"],
        };
        var root = JsonNode.Parse(Resources.Text(resource))!;
        var found = new List<string>();

        // Act.
        Walk(root, "");

        // Assert: the version and every renamed key in one list, so a 0.2 bundle shows both.
        if (root["disl"]?.GetValue<string>() is var disl && disl != "0.3") found.Insert(0, $"/disl is \"{disl}\"");
        Assert.Empty(found);

        void Walk(JsonNode? node, string pointer)
        {
            switch (node)
            {
                case JsonObject members:
                    foreach (var (name, value) in members)
                    {
                        var at = pointer + "/" + name;
                        if (renamed.Contains(name)) found.Add(at);
                        if (keptFor.TryGetValue(name, out var allowed) && value is JsonObject kept)
                        {
                            found.AddRange(kept.Select(member => member.Key).Where(key => !allowed.Contains(key)).Select(key => at + "/" + key));
                        }
                        Walk(value, at);
                    }
                    break;
                case JsonArray items:
                    for (var index = 0; index < items.Count; index++) Walk(items[index], pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }
        }
    }

    [Theory]
    [InlineData(Resources.HypeCycle, "definitions/diagrams/gartner-hype-cycle-graph.dis")]
    [InlineData(Resources.BehaviorModel, "definitions/diagrams/agent-behavior-modelling.dis")]
    public void TheBundledDefinition_CarriesItsProvenance(string resource, string path)
    {
        // Act.
        var provenance = BundledDefinition.Load(Resources.Assembly, resource).Provenance;

        // Assert.
        Assert.Equal("etalii-adp/etalii.adp", provenance.Repository);
        Assert.Equal(path, provenance.Path);
        Assert.Matches("^[0-9a-f]{40}$", provenance.Revision);
    }

    /// <summary>The contexts the walker gives the hype cycle graph's expressions, spot-checked against DISL §12.3 and §8.4.</summary>
    [Theory]
    [InlineData("/constraints/rules/0/rule", DislContexts.Constraint)]
    [InlineData("/constraints/rules/9/rule", DislContexts.GestureChange)]
    [InlineData("/constraints/rules/14/rule", DislContexts.GesturePlacement)]
    [InlineData("/constraints/builtIn/std.references/message/cel", DislContexts.BuiltInMessage)]
    [InlineData("/constraints/builtIn/std.endpoints/refusal/cel", DislContexts.BuiltInRefusal)]
    [InlineData("/toolbox/groups/0/tools/0/initial/start/cel", DislContexts.Create)]
    [InlineData("/toolbox/contextMenus/0/tools/0/visible", DislContexts.Element)]
    [InlineData("/behavior/hooks/0/actions/1/set/peakEnd", DislContexts.Hook)]
    [InlineData("/behavior/operations/addTrendHere/actions/0/create/attributes/start", DislContexts.Operation)]
    [InlineData("/behavior/deletion/Trend/confirm/message/cel", DislContexts.GestureDelete)]
    [InlineData("/notation/shapes/phasedBanner/handles/0/x", DislContexts.Shape)]
    [InlineData("/notation/shapes/phasedBanner/handles/0/visible", DislContexts.Element)]
    [InlineData("/notation/shapes/phasedBanner/handles/0/snap/cel", DislContexts.HandleSnap)]
    [InlineData("/notation/shapes/phasedBanner/handles/0/write/0/set/peakEnd", DislContexts.HandleWrite)]
    [InlineData("/notation/canvas/filters/tags/keep", DislContexts.Filter)]
    [InlineData("/notation/canvas/filters/tags/options/cel", DislContexts.FilterOptions)]
    [InlineData("/notation/nodes/Trigger/labels/0/parse/write/0/set/name", DislContexts.LabelParse)]
    [InlineData("/forms/trend/items/3/items/0/value/cel", DislContexts.Form)]
    public void AnExpressionOfTheHypeCycleGraph_IsCompiledInItsContext(string pointer, string context)
    {
        // Act.
        var expression = BundledDefinition.Load(Resources.Assembly, Resources.HypeCycle).Specification.ExpressionAt(pointer);

        // Assert.
        Assert.NotNull(expression);
        Assert.Equal(context, expression.Context);
    }

    [Theory]
    [InlineData("/metamodel/relations/Child/derived/from", DislContexts.Derive)]
    [InlineData("/metamodel/relations/Child/derived/source", DislContexts.DeriveItem)]
    [InlineData("/persistence/ids/expression", DislContexts.Identity)]
    [InlineData("/behavior/retype/Sequence/attributeMapping/attempts", DislContexts.Retype)]
    [InlineData("/toolbox/contextMenus/0/tools/5/label/cel", DislContexts.Element)]
    [InlineData("/forms/notesDialog/items/0/initial", DislContexts.Form)]
    public void AnExpressionOfTheBehaviorModel_IsCompiledInItsContext(string pointer, string context)
    {
        // Act.
        var expression = BundledDefinition.Load(Resources.Assembly, Resources.BehaviorModel).Specification.ExpressionAt(pointer);

        // Assert.
        Assert.NotNull(expression);
        Assert.Equal(context, expression.Context);
    }

    /// <summary>A call the runtime lacks, planted in each kind of position of each definition, refuses the load and names the function and where.</summary>
    [Theory]
    [InlineData(Resources.HypeCycle, "/constraints/rules/1/rule", "soundex(self.name) == ''")]
    [InlineData(Resources.HypeCycle, "/functions/monthText/cel", "soundex(string(m))")]
    [InlineData(Resources.HypeCycle, "/notation/nodes/Trigger/tooltip/cel", "soundex(self.name)")]
    [InlineData(Resources.BehaviorModel, "/functions/shorten/cel", "soundex(s)")]
    [InlineData(Resources.BehaviorModel, "/metamodel/relations/Child/derived/from", "soundex(diagram.nodes)")]
    [InlineData(Resources.BehaviorModel, "/persistence/ids/expression", "soundex(self.label)")]
    public void ACallOfAFunctionTheRuntimeLacks_RefusesTheDefinition(string resource, string pointer, string planted)
    {
        // Arrange.
        var root = JsonNode.Parse(Resources.Text(resource))!;
        var segments = pointer.Trim('/').Split('/');
        var owner = segments[..^1].Aggregate(root, (node, segment) => int.TryParse(segment, out var index) ? node[index]! : node[segment]!);
        owner[segments[^1]] = planted;

        // Act.
        var result = DislLoader.Load(root.ToJsonString());

        // Assert.
        Assert.Null(result.Specification);
        Assert.Contains(result.Errors, error => error.Pointer == pointer && error.Message.Contains("'soundex'", StringComparison.Ordinal));
    }
}
