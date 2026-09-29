namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>What <see cref="HelmYaml.Read"/> made of one file.</summary>
/// <remarks>
/// A closed set: the two records declared beside this one, following the shape the sibling
/// modules already use. Two cases rather than an exception, because an unreadable file is an
/// ordinary thing for this module to meet - a chart is a folder of them, and one bad file must
/// cost only itself (Requirement 3.1).
/// </remarks>
public abstract record HelmYamlResult;
