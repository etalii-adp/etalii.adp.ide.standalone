namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Where the layout put one element.
/// </summary>
/// <remarks>
/// The position is the element's top-left corner, which is what the core contract's
/// <c>Element.position</c> carries. The layer and row come along because they are what the
/// position means - "third column, second row" survives a change of pitch, and a test that asserts
/// on them is not asserting on arithmetic.
/// </remarks>
/// <param name="Id">The element placed.</param>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Size">How big it is.</param>
/// <param name="Layer">Its column: how deep its longest chain of dependencies runs.</param>
/// <param name="Row">Its place among the elements that share its layer, in declared order.</param>
public sealed record PipelinePlacement(string Id, double X, double Y, PipelineSize Size, int Layer, int Row);
