namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Runs one action on dispose - how the resolver's tracking detaches from the store.</summary>
public sealed class RdfUnsubscriber(Action unsubscribe) : IDisposable
{
    public void Dispose() => unsubscribe();
}
