namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Which tool serves a path, as <see cref="ToolRouter.Route"/> answers (code-structure FR-058): a
/// <see cref="DiagramRouted"/>, a <see cref="DesignerRouted"/>, an <see cref="EditorRouted"/>, a
/// <see cref="ToolUnreadable"/> or <see cref="NoTool"/>. The three kinds' own answers derive from it,
/// so a routed kind is handed on as the router of that kind produced it.
/// </summary>
public abstract record ToolRouting;

/// <summary>
/// A registration that names its tool but cannot be served: unreadable, or a <c>body:</c> header
/// leading outside the project. Refused rather than offered to the next kind, which would present
/// the registration as a file of its own.
/// </summary>
public sealed record ToolUnreadable(string Path, string Reason) : ToolRouting;

/// <summary>No tool serves the path: no diagram or designer type claims it, and no editor does - or more than one does, and none is the default.</summary>
public sealed record NoTool : ToolRouting;
