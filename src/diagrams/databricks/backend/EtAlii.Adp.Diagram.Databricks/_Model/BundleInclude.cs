using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>One <c>include:</c> entry of a bundle - a glob naming resource files to pull in.</summary>
/// <param name="Glob">The glob as written.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record BundleInclude(string Glob, LineRange Lines);
