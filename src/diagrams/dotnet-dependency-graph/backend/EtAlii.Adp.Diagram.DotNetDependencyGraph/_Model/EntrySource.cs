using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Where an entry is in the file it was read from (Q6 of dotnet-dependency-graph-disl-fbl): its 1-based line, and
/// the bytes of its start tag where the file is UTF-8 (<see cref="EntrySpan"/>). The default is an entry read from
/// no file, as a hand-built graph's.
/// </summary>
/// <param name="Line">The 1-based line; 0 when it is not known.</param>
/// <param name="Span">The start tag's bytes; empty where the file is not UTF-8 or the entry is not an element.</param>
public readonly record struct EntrySource(int Line, Span Span);
