namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>What a file under <c>templates/</c> is to Helm.</summary>
public enum TemplateRole
{
    /// <summary>Renders to Kubernetes manifests - the ordinary case.</summary>
    Manifest,

    /// <summary>An underscore-prefixed partial: defines named templates, renders nothing itself.</summary>
    Partial,

    /// <summary><c>NOTES.txt</c>: renders to install notes, not to a manifest.</summary>
    Notes,

    /// <summary>Lives under <c>templates/tests/</c>: a <c>helm test</c> hook.</summary>
    Test,
}
