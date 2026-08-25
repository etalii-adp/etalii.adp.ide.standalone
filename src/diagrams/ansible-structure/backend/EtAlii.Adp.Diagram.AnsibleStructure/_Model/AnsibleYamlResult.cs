namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>What <see cref="AnsibleYaml.Read"/> made of one file.</summary>
/// <remarks>
/// A closed set: the two records declared beside this one, following the shape
/// <c>DiagramRouting</c> and <c>ContextLevelResolution</c> already use in this repository. Two
/// cases rather than an exception, because an unreadable file is an ordinary thing for this
/// module to meet - a diagram is a folder of them, and one bad file must cost only itself.
/// </remarks>
public abstract record AnsibleYamlResult;
