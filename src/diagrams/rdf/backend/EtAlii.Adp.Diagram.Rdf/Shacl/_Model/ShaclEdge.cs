namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>One drawn shape-to-shape reference (shacl-diagram Requirements 1.4, 1.5).</summary>
/// <param name="Id">The element id: <c>shacl-edge:{fromId}|{kind}|{toId}</c>.</param>
/// <param name="FromId">The referring card's element id.</param>
/// <param name="ToId">The referenced card's element id.</param>
/// <param name="Kind">The reference kind: <c>node</c>, <c>class</c>, <c>and</c>, <c>or</c>, <c>xone</c> or <c>not</c>.</param>
/// <param name="Label">The edge label the canvas draws.</param>
public sealed record ShaclEdge(string Id, string FromId, string ToId, string Kind, string Label);
