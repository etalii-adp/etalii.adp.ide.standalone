using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The one owner of loaded <c>.cld</c> files, keyed by path, so two connections on one document
/// share its parse rather than each holding their own.
/// </summary>
/// <remarks>
/// <b>A marker.</b> Everything a store does is declared by <see cref="IDiagramDocumentStore{TBody, TModel}"/>;
/// this names the causal loop diagram's, so a registration and a constructor say which store they mean.
/// The body is the document as lines rather than the model, because every edit in this module is a
/// splice into those lines.
/// </remarks>
public interface ICausalLoopDocumentStore : IDiagramDocumentStore<CausalLoopDocument, CausalLoopModel>;
