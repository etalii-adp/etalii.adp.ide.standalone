using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>The file opens in <paramref name="Definition"/>'s editor.</summary>
public sealed record EditorRouted(EditorDefinition Definition) : EditorRouting;
