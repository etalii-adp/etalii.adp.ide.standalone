using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>What every reading answers the same way, given what it draws.</summary>
public static class RdfReadings
{
    /// <summary>
    /// The element <paramref name="reading"/> draws for <paramref name="elementId"/> in
    /// <paramref name="model"/>, or null when it draws none: its type and payload are what the
    /// canvas was sent, though not its place, which no stored position moves here.
    /// </summary>
    public static DiagramElement? Drawn<TProjection>(this IRdfReading<TProjection> reading, RdfModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var projection = reading.Project(model);
        var positions = reading.Positions(projection, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal));
        return reading.Elements(projection, positions).FirstOrDefault(element => element.Id == elementId);
    }
}
