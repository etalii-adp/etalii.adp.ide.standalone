namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>The five edge families of Requirement 5.</summary>
public enum HelmEdgeKind
{
    /// <summary>Chart to dependency, labeled with the version constraint (R5.1).</summary>
    Declares,

    /// <summary>Dependency to its vendored content - or an open end when Unvendored (R5.2).</summary>
    Resolves,

    /// <summary>An override values layer onto the default one: the stack (R5.3).</summary>
    Overrides,

    /// <summary>The default values onto a dependency its top-level key configures (R5.4).</summary>
    Configures,

    /// <summary>A template onto the partial defining the name it includes - or an open end (R5.6).</summary>
    Includes,
}
