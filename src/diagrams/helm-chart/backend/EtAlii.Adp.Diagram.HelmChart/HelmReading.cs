using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One reading of a chart folder through the binding: what the folder body read (<see cref="Folder"/>) and the
/// definition's model built from it (<see cref="Disl"/>), kept together so that whoever reads one reads the other
/// of the same reading.
/// </summary>
public sealed record HelmReading(FblModel Folder, DislModel Disl)
{
    /// <summary>Whether the folder is a chart: the folder body found it by the binding's <c>recognise</c> and could read it.</summary>
    public bool IsChart => !Folder.Unreadable;
}
