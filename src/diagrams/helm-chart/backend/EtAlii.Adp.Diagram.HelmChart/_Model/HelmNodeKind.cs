namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>Which kind of box a node is - one per element type the wire declares.</summary>
public enum HelmNodeKind
{
    /// <summary>The chart itself: Chart.yaml's facts.</summary>
    Chart,

    /// <summary>One values layer - the default or an override.</summary>
    Values,

    /// <summary>The values.schema.json, represented but never evaluated.</summary>
    Schema,

    /// <summary>One file under templates/ - any role; the role travels in the payload.</summary>
    Template,

    /// <summary>The crds/ folder, summarized as one box.</summary>
    Crds,

    /// <summary>One declared dependency.</summary>
    Dependency,

    /// <summary>An unpacked chart directory under charts/.</summary>
    Subchart,

    /// <summary>A sealed .tgz under charts/ - labeled, never unpacked.</summary>
    Archive,

    /// <summary>The Chart.lock (or requirements.lock).</summary>
    Lock,
}
