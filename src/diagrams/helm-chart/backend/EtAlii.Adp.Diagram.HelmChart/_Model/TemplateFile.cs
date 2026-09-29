namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>One file under <c>templates/</c>, scanned but never parsed as YAML (Requirement 3.2).</summary>
/// <param name="RelativePath">Chart-root-relative.</param>
/// <param name="Role">What the file is to Helm, decided by name and place.</param>
/// <param name="Facts">What is literally written in it, per <see cref="TemplateScan"/>.</param>
public sealed record TemplateFile(string RelativePath, TemplateRole Role, TemplateFacts Facts);
