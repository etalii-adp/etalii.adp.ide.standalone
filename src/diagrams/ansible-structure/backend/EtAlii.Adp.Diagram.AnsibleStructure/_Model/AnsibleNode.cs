namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One box in the diagram, with the identity everything downstream agrees on.
/// </summary>
/// <param name="Id">
/// Stable across any edit that does not move this thing: a discriminator and the folder-relative
/// path or name, e.g. <c>role:nginx</c>, <c>play:webservers.yml#0</c>,
/// <c>taskfile:roles/nginx/tasks/tls.yml</c>. What keeps a selection alive across a watcher push.
/// </param>
/// <param name="Kind">Which node kind this is.</param>
/// <param name="Name">What a reader calls it.</param>
/// <param name="RelativePath">
/// The file or folder it stands for, relative to the diagram's folder - what the canvas hands to
/// <c>revealPath</c> when the node is activated (Requirement 8.1).
/// </param>
/// <param name="PlayIndex">
/// Which play this belongs to, for the colour continuity of Requirement 6.3; -1 when it belongs
/// to none. An index rather than a colour: styling stays in the client's stylesheet.
/// </param>
public sealed record AnsibleNode(
    string Id,
    AnsibleNodeKind Kind,
    string Name,
    string RelativePath,
    int PlayIndex = -1);
