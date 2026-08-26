namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One relationship as the files declare it - before anything tries to resolve what it points
/// at. The reader produces these; <see cref="AnsibleGraph"/> turns them into edges.
/// </summary>
/// <param name="Kind">Which directive wrote it.</param>
/// <param name="Target">
/// The target exactly as written, including a <c>{{ expression }}</c>. Never evaluated and never
/// normalised: the label a reader sees has to be the text they will find in the file.
/// </param>
/// <param name="Condition">
/// The <c>when:</c> guarding it, as written, or empty when there is none. Also never evaluated -
/// ADP does not have the variables, and a guess would be a lie a reader could not detect.
/// </param>
/// <param name="DeclaredIn">The file that wrote it, relative to the diagram's folder.</param>
/// <param name="Line">The 1-based line it was written on, for "why is this here".</param>
public sealed record AnsibleDirective(
    AnsibleDirectiveKind Kind,
    string Target,
    string Condition,
    string DeclaredIn,
    uint Line)
{
    /// <summary>
    /// Whether Ansible resolves this during the run rather than before it - the
    /// <c>include_*</c> family. Drawn dashed where the static kinds are drawn solid, which is
    /// the convention ansible-playbook-grapher's users already read (Requirement 5.4).
    /// </summary>
    public bool IsDynamic => Kind is AnsibleDirectiveKind.IncludeRole or AnsibleDirectiveKind.IncludeTasks;

    /// <summary>
    /// Whether the target is a Jinja expression, and so unknowable without running Ansible.
    /// Such a target is shown as written and marked unresolvable - never reported as missing,
    /// which would fire on every parameterised role in every real repository (Requirement 3.4).
    /// </summary>
    public bool IsExpression => Target.Contains("{{", StringComparison.Ordinal);
}
