namespace EtAlii.Adp.Editor;

/// <summary>
/// One item an analyser offers at a position: what is shown (<paramref name="Label"/>, with
/// <paramref name="Detail"/> beside it and <paramref name="Info"/> as its documentation), the
/// range of UTF-16 offsets it replaces, what is inserted (which may hold <c>${1}</c> tab stops),
/// and a <paramref name="Boost"/> that ranks it, higher first.
/// </summary>
public sealed record TextCompletion(string Label, string Detail, string Info, int From, int To, string Insert, int Boost = 0);
