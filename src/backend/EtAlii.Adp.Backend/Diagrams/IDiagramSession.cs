using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>
/// One open diagram for one connection, from the type module's side. The core
/// <c>DiagramService</c> owns the stream, the authorization and the connection lifetime; a
/// module owns what a diagram <em>is</em> - its elements, its layout, what a viewport shows -
/// and expresses every change as a <see cref="DiagramDelta"/>. This is the fourth registration
/// seam a diagram type has, alongside its definition, its resolver and its providers
/// (mindmap-diagram Requirement 13).
/// </summary>
public interface IDiagramSession : IAsyncDisposable
{
    /// <summary>The baseline: an add for everything currently in the connection's view (grpc-core-communication Requirement 3.2).</summary>
    IReadOnlyList<DiagramDelta> Baseline();

    /// <summary>Narrows or widens what is delivered to this connection; returns the deltas that brings it into line.</summary>
    IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport);

    /// <summary>
    /// Moves an element under a new parent, at <paramref name="index"/> among its children
    /// (negative appends). The module dispatches this as a command through the project's
    /// history, so a drag on the canvas is one undo away like every other edit. Returns the
    /// empty string on success, or the module's own reason for refusing.
    /// </summary>
    Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken);

    /// <summary>
    /// Moves an element to a place on the canvas, for a diagram that is arranged rather than
    /// nested. Dispatched as a command like every other edit, and returns the empty string on
    /// success or the module's own reason for refusing.
    /// </summary>
    /// <remarks>
    /// A separate method rather than more arguments on the one above, because the two gestures
    /// are different questions and a type usually answers only one. A mindmap has no free
    /// coordinates to land on - its tree is its layout - so it refuses this; a C4 element has
    /// no parent a drag may change - a container belongs to the system that declares it - so it
    /// refuses the other. Refusing by default means a type that has thought about neither says
    /// so rather than appearing to support both.
    /// </remarks>
    Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken) =>
        Task.FromResult("This diagram cannot be arranged by dragging.");

    /// <summary>
    /// Raised when something changes the diagram - an edit from any connection, an external
    /// file edit, this connection's own fold - with the deltas that carry it into this
    /// connection's view. The core service writes them to the stream.
    /// </summary>
    event EventHandler<DiagramDeltasEventArgs>? Changed;
}

/// <summary>
/// Opens an <see cref="IDiagramSession"/> for one diagram of one type. A module registers one,
/// keyed by the origin it serves; the core service resolves it from the diagram's declared
/// type and never learns anything type-specific itself.
/// </summary>
public interface IDiagramSessionFactory
{
    /// <summary>The diagram type this opens sessions for.</summary>
    DiagramOrigin Origin { get; }

    /// <summary>
    /// A session for the diagram whose body is at <paramref name="bodyPath"/>, seen by the
    /// connection <paramref name="watchId"/> within the project rooted at
    /// <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="bodyPath">The path to the diagram body file.</param>
    /// <param name="registrationPath">
    /// The <c>.adp</c> file this was opened through, or null for a body opened without one.
    /// Core passes it and interprets nothing in it: a type whose registration carries more
    /// than a MIME line - a C4 view key, say - reads its own headers from here, which keeps
    /// that knowledge in the module where it belongs (c4-diagrams Requirement 2.4).
    /// </param>
    /// <param name="watchId">The connection that opened this session.</param>
    /// <param name="rootPath">The root path of the project.</param>
    IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath);
}
