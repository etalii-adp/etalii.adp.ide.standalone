using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>One declared bundle variable.</summary>
/// <param name="Name">The variable's name, the key under <c>variables:</c>.</param>
/// <param name="Default">Its default value as written; empty when it has none.</param>
/// <param name="Description">Its description; empty when it has none.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record BundleVariable(string Name, string Default, string Description, LineRange Lines);
