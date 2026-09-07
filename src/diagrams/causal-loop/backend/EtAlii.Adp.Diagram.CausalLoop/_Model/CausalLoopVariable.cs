using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>One named quantity in the system - what a causal link runs between.</summary>
/// <param name="Id">The identifier links and loops refer to it by.</param>
/// <param name="Label">What a reader sees on the canvas; falls back to the id when unwritten.</param>
/// <param name="Lines">Where the declaration sits, so a writer splices rather than reserializes.</param>
public sealed record CausalLoopVariable(string Id, string Label, LineRange Lines)
{
    /// <summary>What the canvas draws: the label where there is one, the id otherwise.</summary>
    public string Display => Label.Length > 0 ? Label : Id;
}
