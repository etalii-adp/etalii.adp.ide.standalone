using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The nodes and relations of an open folder's last reading, under the name the module's graph had. The graph left
/// the product with the reading through the binding (helm-chart-disl-fbl, task 9); <c>ZeroWrites.Tests.cs</c> walks
/// every element through it and is never edited (tasks.md, task 1), so the name stays in the test project for it
/// and answers with the definition's model, which is what the product draws. Today's graph is the oracle's,
/// <see cref="Parity.HelmGraph"/>.
/// </summary>
internal sealed class HelmGraph
{
    private HelmGraph(DislDiagram diagram)
    {
        Nodes = diagram.Nodes;
        Edges = HelmElementMapper.Relations(diagram);
    }

    /// <summary>Every node of the model, in the order the plugin delivered them.</summary>
    public IReadOnlyList<DislElement> Nodes { get; }

    /// <summary>Every relation of the model, in the order the mapper delivers them.</summary>
    public IReadOnlyList<DislElement> Edges { get; }

    /// <summary>The model of <paramref name="body"/>'s last reading.</summary>
    public static HelmGraph Derive(HelmBody body) => new(body.Current.Disl.Diagram);
}
