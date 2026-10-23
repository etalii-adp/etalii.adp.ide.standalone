namespace EtAlii.Adp.Designer;

/// <summary>What a hierarchy surface session raises: the whole model after a change, or what became of a gesture.</summary>
public sealed class HierarchySurfaceChangedEventArgs : EventArgs
{
    public HierarchySurfaceChangedEventArgs(HierarchySurfaceModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public HierarchySurfaceChangedEventArgs(HierarchySurfaceEditOutcome outcome)
    {
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
    }

    /// <summary>The whole model, when this is a change; null when it is an outcome.</summary>
    public HierarchySurfaceModel? Model { get; }

    /// <summary>What became of a gesture, when this is an outcome; null when it is a change.</summary>
    public HierarchySurfaceEditOutcome? Outcome { get; }
}
