namespace EtAlii.Adp.Sessions;

/// <summary>
/// One open designer document on one connection, whatever surface draws it: what the sessions of a table and of a
/// hierarchy surface share (spec 029 R8). Each kind's own members are on <see cref="ITableDesignerSession"/> and
/// <see cref="IHierarchySurfaceSession"/>.
/// </summary>
/// <remarks>
/// A session is per connection and ends with its stream: two tabs on one document are two sessions over the one
/// document. A session meets the obligations an editor session meets towards the file underneath it - a change made
/// outside ADP reaches it and is pushed to the connection.
/// </remarks>
public interface IDesignerSession : IAsyncDisposable;
