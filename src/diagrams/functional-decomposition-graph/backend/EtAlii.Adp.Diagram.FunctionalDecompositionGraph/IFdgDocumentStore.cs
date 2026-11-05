using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The loaded <c>.fdg</c> documents, one per body path and shared by every session viewing it.
/// </summary>
/// <remarks>
/// <b>A marker.</b> Everything a store does is declared by <see cref="IDiagramDocumentStore{TBody, TModel}"/>;
/// this type is what the module's registrations, its reloader and the shared restore command
/// resolve, so a document of another type can never reach them.
/// </remarks>
public interface IFdgDocumentStore : IDiagramDocumentStore<FdgBody, DislModel>;
