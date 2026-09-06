using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Hierarchy;

/// <summary>The file opens in <paramref name="Definition"/>'s editor.</summary>
public sealed record EditorRouted(EditorDefinition Definition) : EditorRouting;
