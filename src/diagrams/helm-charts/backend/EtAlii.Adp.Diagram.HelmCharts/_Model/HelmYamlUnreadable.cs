namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>The file did not parse, and this is what the parser said about it.</summary>
public sealed record HelmYamlUnreadable(HelmYamlFailure Failure) : HelmYamlResult;
