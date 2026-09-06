using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram;

/// <summary>
/// The Toolbox entries one diagram type contributes. A module registers exactly one of
/// these - the same one-line registration its document factory and action provider use -
/// and the shared diagram service answers <c>DescribeToolbox</c> from it by origin,
/// without core learning what the type is. A type that registers none simply has an
/// empty toolbox, which is a valid answer, not an error.
/// </summary>
public interface IDiagramToolboxProvider
{
    /// <summary>The diagram type whose toolbox this describes.</summary>
    DiagramOrigin Origin { get; }

    /// <summary>The entries, in the order the palette shows them. Static per type.</summary>
    IReadOnlyList<ToolboxItemDefinition> Items { get; }
}
