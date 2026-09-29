using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>The file parsed, and its root node when it had one.</summary>
/// <param name="Root">
/// Null for an empty file. That is not a failure: an empty <c>.yaml</c> is valid YAML with no
/// document in it, and the reader simply finds nothing in it to recognise.
/// </param>
public sealed record HelmYamlDocument(YamlNode? Root) : HelmYamlResult;
