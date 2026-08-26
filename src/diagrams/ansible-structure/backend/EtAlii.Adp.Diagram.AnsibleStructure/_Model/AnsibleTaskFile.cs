namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One of a role's task files. Drawn as its own node only when something includes it
/// (Requirement 4.1); the rest are counted in the role's summary and nothing more.
/// </summary>
/// <param name="Name">The file name, e.g. <c>tls.yml</c>.</param>
/// <param name="RelativePath">The file, relative to the diagram's folder.</param>
public sealed record AnsibleTaskFile(string Name, string RelativePath);
