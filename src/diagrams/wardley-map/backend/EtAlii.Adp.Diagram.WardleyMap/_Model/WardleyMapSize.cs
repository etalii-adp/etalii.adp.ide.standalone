namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The `size [w, h]` a map asks to be drawn at, in pixels (Requirement 5.6). A property of the
/// map rather than an element on it.
/// </summary>
public sealed record WardleyMapSize(double Width, double Height);
