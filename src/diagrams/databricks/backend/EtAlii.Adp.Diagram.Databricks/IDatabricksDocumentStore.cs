using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The one owner of Databricks configuration documents on disk: one per open file, keyed by
/// path, so the family's three diagram types share a file's document and an edit made through
/// one is visible in the others.
/// </summary>
/// <remarks>
/// One store for all three formats, deliberately: <c>databricks.yml</c>, job resource YAML and
/// pipeline settings JSON are one configuration family, a single file legitimately serves more
/// than one diagram (a bundle declaring a job inline is both), and JSON reads through the same
/// YAML door - so three stores would be three copies of the same lifecycle keyed by the same
/// paths. Everything it does is the shared store's; this interface is the distinct type the
/// registrations and the restore command resolve.
/// </remarks>
public interface IDatabricksDocumentStore : IDiagramDocumentStore<LineDocument, DatabricksModel>;
