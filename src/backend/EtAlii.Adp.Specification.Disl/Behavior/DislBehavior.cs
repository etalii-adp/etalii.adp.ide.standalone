namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What <c>behavior</c> declares besides its operations (DISL §9), typed at load: the hooks and their depth, the
/// standard messages and the named reasons, and the deletion and retype policies.
/// </summary>
/// <param name="Hooks">The hooks that are objects, in declaration order.</param>
/// <param name="MaxHookDepth">The <c>maxHookDepth</c>, 100 when it states none or none an int holds.</param>
/// <param name="MaxHookDepthMalformed">The message System.Text.Json threw reading a <c>maxHookDepth</c> that is not a number, null otherwise.</param>
/// <param name="Messages">The <c>messages</c> (§9.1), by key.</param>
/// <param name="MessagesMalformed">The message System.Text.Json threw looking a key up in <c>messages</c> that is not an object, null otherwise.</param>
/// <param name="Reasons">The <c>reasons</c> (§2.3), by id.</param>
/// <param name="ReasonsMalformed">The message System.Text.Json threw looking an id up in <c>reasons</c> that is not an object, null otherwise.</param>
/// <param name="Deletion">The members of <c>deletion</c> (§9.5), by type name, in declaration order; none when it is not an object.</param>
/// <param name="Retype">The members of <c>retype</c> (§9.5), by type name, in declaration order; none when it is not an object.</param>
/// <param name="Malformed">
/// The message System.Text.Json threw when the runtime read a member of a <c>behavior</c> that is not an object, null for
/// one that is; such a behavior still throws it wherever one of these was read.
/// </param>
internal sealed record DislBehavior(
    IReadOnlyList<DislHook> Hooks,
    int MaxHookDepth,
    string? MaxHookDepthMalformed,
    IReadOnlyDictionary<string, DislMessage> Messages,
    string? MessagesMalformed,
    IReadOnlyDictionary<string, DislReason> Reasons,
    string? ReasonsMalformed,
    IReadOnlyList<(string Type, DislDeletionPolicy Policy)> Deletion,
    IReadOnlyList<(string Type, DislRetypePolicy Policy)> Retype,
    string? Malformed)
{
    private const int DefaultMaxHookDepth = 100;

    public static DislBehavior None { get; } = new([], DefaultMaxHookDepth, null, new Dictionary<string, DislMessage>(), null, new Dictionary<string, DislReason>(), null, [], [], null);

    /// <summary>The message <c>messages</c> gives for <paramref name="key"/>, or null when it gives none.</summary>
    public DislMessage? Message(string key)
    {
        Throw(Malformed);
        Throw(MessagesMalformed);
        return Messages.GetValueOrDefault(key);
    }

    /// <summary>The reason <c>reasons</c> declares as <paramref name="id"/>, or null when it declares none.</summary>
    public DislReason? Reason(string id)
    {
        Throw(Malformed);
        Throw(ReasonsMalformed);
        return Reasons.GetValueOrDefault(id);
    }

    /// <summary>How many hooks a transaction may run (§9.2).</summary>
    public int HookDepth()
    {
        Throw(Malformed);
        Throw(MaxHookDepthMalformed);
        return MaxHookDepth;
    }

    /// <summary>The deletion policy of the first of <paramref name="linearisation"/> that declares one, and its pointer; null when none does.</summary>
    public DislDeletionPolicy? DeletionOf(IReadOnlyList<string> linearisation)
    {
        Throw(Malformed);
        foreach (var type in linearisation)
        {
            if (Deletion.LastOrDefault(entry => entry.Type == type).Policy is { } policy) return policy;
        }
        return null;
    }

    /// <summary>The retype policy of the first of <paramref name="linearisation"/> that declares one as an object; null when none does.</summary>
    public DislRetypePolicy? RetypeOf(IReadOnlyList<string> linearisation)
    {
        Throw(Malformed);
        foreach (var type in linearisation)
        {
            if (Retype.LastOrDefault(entry => entry.Type == type).Policy is { IsObject: true } policy) return policy;
        }
        return null;
    }

    private static void Throw(string? malformed)
    {
        if (malformed is not null) throw new InvalidOperationException(malformed);
    }
}

/// <summary>A hook of <c>behavior.hooks</c> (DISL §9.2), typed at load.</summary>
/// <param name="Pointer">Where the hook is, which its id is when it states none.</param>
/// <param name="Id">Its <c>id</c>, else its pointer.</param>
/// <param name="On">The events it runs on.</param>
/// <param name="Phase">Its <c>phase</c>, <c>after</c> when it states none.</param>
/// <param name="For">The types it runs for; none for any.</param>
/// <param name="Attribute">The attributes whose change it runs on; none for any.</param>
/// <param name="Order">Its <c>order</c>, 0 when it states none or none a long holds.</param>
/// <param name="OrderMalformed">The message System.Text.Json threw reading an <c>order</c> that is not a number, null otherwise.</param>
/// <param name="When">Its condition, or null when it has none.</param>
/// <param name="Actions">Its actions, or null when it states none.</param>
internal sealed record DislHook(
    string Pointer,
    string Id,
    IReadOnlyList<string> On,
    string Phase,
    IReadOnlyList<string> For,
    IReadOnlyList<string> Attribute,
    long Order,
    string? OrderMalformed,
    DislExpressionField? When,
    DislActionList? Actions);

/// <summary>A deletion policy of <c>behavior.deletion</c> (DISL §9.5), typed at load.</summary>
/// <param name="Children">Its <c>children</c>, or null for the default.</param>
/// <param name="Relations">Its <c>relations</c>, or null for the default.</param>
/// <param name="References">Its <c>references</c>, or null for the default.</param>
/// <param name="Confirm">Its <c>confirm</c>, or null when it asks nothing.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read a policy that is not an object, null for one that is; such a
/// policy still throws it where its <c>confirm</c> was read.
/// </param>
internal sealed record DislDeletionPolicy(string? Children, string? Relations, string? References, DislDeletionConfirm? Confirm, string? NotAnObject);

/// <summary>A deletion's <c>confirm</c> (DISL §9.5), typed at load.</summary>
internal abstract record DislDeletionConfirm
{
    /// <summary>A Message, asked every time.</summary>
    public sealed record Always(DislMessage Message) : DislDeletionConfirm;

    /// <summary>
    /// An object with a <c>message</c>, asked while its <c>count</c> reaches its <c>threshold</c> and its <c>when</c> holds.
    /// </summary>
    /// <param name="ThresholdMalformed">The message System.Text.Json threw reading a <c>threshold</c> that is not a number, null otherwise.</param>
    public sealed record Asked(
        DislExpressionField? Count,
        long Threshold,
        string? ThresholdMalformed,
        DislExpressionField? When,
        DislMessage? Title,
        DislMessage Message,
        DislMessage? ConfirmLabel,
        bool Danger) : DislDeletionConfirm;
}

/// <summary>A retype policy of <c>behavior.retype</c> (DISL §9.5), typed at load.</summary>
/// <param name="IsObject">Whether the policy is an object; one that is not is passed over.</param>
/// <param name="To">The types it may become.</param>
/// <param name="AttributeMapping">Its <c>attributeMapping</c>: per attribute of the new type, the Expression its value is.</param>
internal sealed record DislRetypePolicy(bool IsObject, IReadOnlyList<string> To, IReadOnlyList<(string Attribute, DislExpressionField Value)> AttributeMapping);
