using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>One <c>libraries:</c> entry - a source the pipeline's transformations come from.</summary>
/// <param name="Kind">How the entry names its source - <c>notebook</c>, <c>file</c>, <c>glob</c> - or <c>other</c>.</param>
/// <param name="Path">The path or include pattern as written.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record PipelineLibrary(string Kind, string Path, LineRange Lines);
