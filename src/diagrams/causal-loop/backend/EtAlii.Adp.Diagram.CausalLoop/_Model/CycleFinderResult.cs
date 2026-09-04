namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>What a cycle search produced.</summary>
/// <param name="Cycles">Each cycle as the variables it runs through, in order, starting from its least member.</param>
/// <param name="Truncated">Whether the bound stopped the search before it finished.</param>
/// <param name="Examined">How many cycles were enumerated - what the reader is told when the search was cut short.</param>
public sealed record CycleFinderResult(
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    bool Truncated,
    int Examined);
