namespace EtAlii.Adp.Common;

/// <summary>
/// The problem sits in a file other than the diagram's own, named relative to the project root,
/// optionally at a line.
/// </summary>
/// <remarks>
/// <para>
/// The case a <see cref="DiagramSubject.Folder"/> type has and no earlier type could: its
/// diagram is a tree of files, so "which file is this wrong in" has an answer that is neither
/// the registration nor a line in it. An Ansible role named by a playbook but missing from
/// <c>roles/</c> is a mistake in the playbook that named it, and pointing the reader at the
/// <c>.adp</c> instead would send them to a one-line file with nothing to fix in it.
/// </para>
/// <para>
/// Core does two things with this rather than one, and the second matters more: the problem is
/// **attributed** to the named file, so the panel reveals it; and the file is what the entry's
/// staleness is **pinned** to, so editing it marks the verdict stale. Pinning a folder diagram's
/// every problem to a registration that never changes would leave every verdict looking fresh
/// for ever.
/// </para>
/// </remarks>
/// <param name="RelativePath">
/// Project-relative, never absolute - the client never sees a filesystem path, and two viewers
/// of one project must read the same locator. Core containment-checks it before using it.
/// </param>
/// <param name="Line">The 1-based line, or 0 when the rule can only name the file.</param>
public sealed record DiagramProblemFileLocation(string RelativePath, uint Line = 0) : DiagramProblemLocation;
