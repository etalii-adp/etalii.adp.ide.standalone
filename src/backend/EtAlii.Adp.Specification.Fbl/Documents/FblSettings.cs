using JetBrains.Annotations;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>What writing an empty value does (FBL §5.2 <c>empty</c>, §6.2): write it, leave its slot out, or refuse.</summary>
public enum EmptyRule
{
    Keep,
    Remove,
    Refuse,
}

/// <summary>What setting a value does when its slot is absent from the entry (FBL §5.2 <c>absent</c>).</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // Every case is read from a binding by its name, whether or not code names it.
public enum AbsentRule
{
    Insert,
    Refuse,
}

/// <summary>What a yaml or json slot reads from a scalar (FBL 0.5 §5.2 <c>read</c>).</summary>
public enum ReadAs
{
    Text,
    Typed,
}

/// <summary>Whether a quoted yaml scalar read as typed converts from its written text (FBL 0.5 §5.2 <c>quoted</c>).</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // Every case is read from a binding by its name, whether or not code names it.
public enum QuotedRule
{
    Text,
    Convert,
}

/// <summary>The style a new yaml scalar is written in (FBL §5.2 <c>style</c>).</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // Every case is read from a binding by its name, whether or not code names it.
public enum WrittenStyle
{
    Plain,
    Double,
    Single,
    Literal,
    Flow,
}

/// <summary>The quoting of a new yaml scalar that cannot be plain (FBL §6.3 <c>text.quote</c>).</summary>
public enum QuoteStyle
{
    Double,
    Single,
}

/// <summary>Where a new entry goes (FBL §6.2 <c>insert.place</c>); <see cref="Before"/> is <c>{before: key}</c>.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // Every case is read from a binding by its name, whether or not code names it.
public enum InsertPlace
{
    AfterLast,
    End,
    Start,
    LastChild,
    NextSibling,
    EndOfDocument,
    AfterHeader,
    Before,
}

/// <summary>Where a missing xml child holding a value is created (FBL §5.2 <c>create.place</c>); <see cref="Before"/> is <c>{before: name}</c>.</summary>
public enum ChildPlace
{
    First,
    Last,
    Before,
}

/// <summary>Where a missing container is created (FBL §6.2 <c>insert.create.at</c>).</summary>
public enum ContainerPlace
{
    EndOfDocument,
    Before,
    After,
    Under,
    End,
}

/// <summary>A setting's name as a binding writes it, and back: <c>AfterLast</c> is <c>after-last</c>.</summary>
public static class WireName
{
    public static string Of<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var wire = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && wire.Length > 0) wire.Append('-');
            wire.Append(char.ToLowerInvariant(c));
        }
        return wire.ToString();
    }

    public static bool TryParse<T>(string? wire, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (Of(candidate) != wire) continue;
            value = candidate;
            return true;
        }
        value = default;
        return false;
    }
}
