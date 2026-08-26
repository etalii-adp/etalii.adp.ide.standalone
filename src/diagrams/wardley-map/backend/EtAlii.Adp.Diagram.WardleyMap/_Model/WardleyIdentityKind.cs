namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What sort of thing an identity names. Two elements of different kinds may legitimately share
/// a key - a component and a note can hold the same text - so the kind is part of the match.
/// </summary>
public static class WardleyIdentityKind
{
    public const string Component = "component";
    public const string Link = "link";
    public const string Pipeline = "pipeline";
    public const string PipelineChild = "pipeline-child";
    public const string Note = "note";
    public const string Annotation = "annotation";
}
