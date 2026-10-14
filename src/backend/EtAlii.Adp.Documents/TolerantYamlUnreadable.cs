namespace EtAlii.Adp.Documents;

/// <summary>The file did not parse, and this is what the parser said about it.</summary>
public sealed record TolerantYamlUnreadable(TolerantYamlFailure Failure) : TolerantYamlResult;
