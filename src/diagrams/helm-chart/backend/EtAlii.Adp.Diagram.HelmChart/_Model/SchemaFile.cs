namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The <c>values.schema.json</c> beside the values stack. Represented, never evaluated:
/// JSON-Schema validation of values is deliberately out of scope (the requirements refuse the
/// dependency), so all the diagram claims about it is that it exists.
/// </summary>
/// <param name="RelativePath">Chart-root-relative.</param>
public sealed record SchemaFile(string RelativePath);
