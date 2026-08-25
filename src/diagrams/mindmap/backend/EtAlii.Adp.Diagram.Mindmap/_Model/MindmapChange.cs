namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What a command did to a document, in the terms the delta mapper needs: which nodes to
/// re-send, which are gone. A structural change relays out the whole map, so it names
/// nothing and the mapper re-derives everything from the layout.
/// </summary>
/// <remarks>A closed set: the three <c>Mindmap*</c> records declared beside this one.</remarks>
public abstract record MindmapChange;
