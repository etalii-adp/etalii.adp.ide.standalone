namespace EtAlii.Adp.Problems;

/// <summary>
/// What one validation request covers: a single diagram, a folder and everything beneath it,
/// or the whole project. Paths are project-relative; the root anchors them.
/// </summary>
/// <remarks>A closed set: the three <c>*ValidationScope</c> records declared beside this one.</remarks>
public abstract record ValidationScope(string RootPath);
