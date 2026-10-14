namespace EtAlii.Adp.Documents;

/// <summary>What <see cref="TolerantYaml.Read"/> made of one file.</summary>
/// <remarks>
/// A closed set: <see cref="TolerantYamlDocument"/> and <see cref="TolerantYamlUnreadable"/>. Two
/// cases rather than an exception, because an unreadable file is an ordinary thing for a folder
/// diagram to meet - one bad file must cost only itself.
/// </remarks>
public abstract record TolerantYamlResult;
