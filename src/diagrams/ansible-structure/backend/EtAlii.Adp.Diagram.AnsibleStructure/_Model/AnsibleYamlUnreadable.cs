namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>The file did not parse, and this is what the parser said about it.</summary>
public sealed record AnsibleYamlUnreadable(AnsibleYamlFailure Failure) : AnsibleYamlResult;
