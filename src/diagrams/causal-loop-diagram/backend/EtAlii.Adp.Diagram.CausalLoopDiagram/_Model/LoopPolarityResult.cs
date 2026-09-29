namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>What the arrows say a loop is, as opposed to what the document calls it.</summary>
public enum LoopPolarityResult
{
    /// <summary>Some link around the cycle states no polarity, so the parity cannot be counted.</summary>
    Undecidable = 0,

    /// <summary>An even number of negative links - zero included. The loop amplifies.</summary>
    Reinforcing,

    /// <summary>An odd number of negative links. The loop self-corrects.</summary>
    Balancing,
}
