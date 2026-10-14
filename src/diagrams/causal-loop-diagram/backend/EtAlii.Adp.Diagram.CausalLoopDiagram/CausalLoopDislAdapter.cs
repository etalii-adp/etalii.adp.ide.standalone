using System.Runtime.CompilerServices;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The DISL model of what <see cref="CausalLoopParser"/> read, built through <see cref="DislModelBuilder"/>
/// from a reading shaped as the module's binding will give it, so that the menus and the rows are
/// derived from the definition while the body is still read by the parser (causal-loop-disl-fbl
/// task 5). It goes when the body is read through the binding (task 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>The reading is the binding's</b>: a <c>Variable</c> written with its name as its id, a <c>Link</c>
/// with its two ends as <c>from</c> and <c>to</c> and the names it writes them with, a <c>Loop</c> with
/// its identifier as its id and its members as the names it lists. The definition's <c>typeMap</c> and
/// id strategy do the rest, so the model's ids are the wire ids (<c>variable:</c>, <c>link:</c>,
/// <c>loop:</c>) and nothing here computes one.
/// </para>
/// <para>
/// <b>An unstated polarity and an absent weight are left unset</b>, as the binding leaves a word it
/// does not find, so they read as the attribute's default and as absent.
/// </para>
/// </remarks>
internal static class CausalLoopDislAdapter
{
    private static readonly ConditionalWeakTable<CausalLoopModel, DislDiagram> Built = [];

    /// <summary>The model of <paramref name="model"/>, built once per parsed model; read it, never change it.</summary>
    public static DislDiagram Of(CausalLoopModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Built.GetValue(model, Build);
    }

    private static DislDiagram Build(CausalLoopModel model)
    {
        var read = new List<FblElement>();
        foreach (var variable in model.Variables)
        {
            read.Add(Element(variable.Id, true, "Variable", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = variable.Id,
                ["label"] = variable.Label,
            }, variable.Lines));
        }

        foreach (var link in model.Links)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["from"] = link.From,
                ["to"] = link.To,
                ["causeName"] = link.From,
                ["effectName"] = link.To,
                ["delayed"] = link.Delayed,
                ["flipped"] = link.Flipped,
                ["note"] = link.Label,
            };
            if (link.Polarity != CausalLoopPolarity.Unstated) attributes["polarity"] = link.Polarity == CausalLoopPolarity.Positive ? "positive" : "negative";
            if (link.Weight is { } weight) attributes["weight"] = weight;
            read.Add(Element($"{link.From}|{link.To}", false, "Link", attributes, link.Lines));
        }

        foreach (var loop in model.Loops)
        {
            read.Add(Element(loop.Identifier, true, "Loop", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["identifier"] = loop.Identifier,
                ["title"] = loop.Name,
                ["members"] = loop.Variables.Cast<object?>().ToList(),
            }, loop.Lines));
        }

        return DislModelBuilder.From(new FblModel(read, [], false), CausalLoopDefinition.Specification).Diagram;
    }

    private static FblElement Element(string id, bool stored, string type, IReadOnlyDictionary<string, object?> attributes, LineRange lines) =>
        new(id, stored, type, type.ToLowerInvariant(), false, attributes, null, null, null, null, default, lines.Start + 1);
}
