using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The CEL functions of the definition's plugin <c>net.etalii.adp.etalii.supplyChainWritten</c> (DISL §13.1.1):
/// <c>written</c>, the text a key of an entry is written with, and <c>laterHolder</c>, whether an earlier entry holds
/// the id an entry is written with, answered from the model <see cref="SupplyChainReading"/> builds.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is written is kept beside the model</b>, because the model only knows what resolves: a reference to a
/// group that is not there is unset, an end that names no node is unset, and a later holder of an id has an
/// ephemeral one. The id as written is the host attribute <see cref="StoredId"/>, a node's group as written is
/// <see cref="WrittenGroup"/>, a flow's ends as written are <see cref="WrittenFrom"/> and <see cref="WrittenTo"/>.
/// </para>
/// </remarks>
internal static class SupplyChainWritten
{
    /// <summary>The host attribute holding the id an entry is written with, empty for an entry without one.</summary>
    public const string StoredId = "storedId";

    /// <summary>The host attribute holding the group a node is written in, also when no group has that id.</summary>
    public const string WrittenGroup = "writtenGroup";

    /// <summary>The host attribute holding the id a flow's <c>from</c> is written with, empty for none.</summary>
    public const string WrittenFrom = "writtenFrom";

    /// <summary>The host attribute holding the id a flow's <c>to</c> is written with, empty for none.</summary>
    public const string WrittenTo = "writtenTo";

    /// <summary>The host attribute set on an entry whose id an earlier entry holds.</summary>
    public const string LaterHolder = "laterHolder";

    /// <summary>The implementations, for <see cref="DislDiagramDefinition"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("written", arguments => Written(arguments[0], arguments[1]))
        .Add("laterHolder", arguments => IsLaterHolder(arguments[0]));

    /// <summary>The id <paramref name="element"/> is written with: empty for an entry without one.</summary>
    public static string IdOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Host(element, StoredId);
    }

    private static string Written(object? argument, object? key)
    {
        if (argument is not DislElement element || key is not string name)
        {
            throw new CelException("written() takes an entry of this diagram and the key it is written with.");
        }

        return name switch
        {
            "id" => Host(element, StoredId),
            "group" => Host(element, WrittenGroup),
            "from" => Host(element, WrittenFrom),
            "to" => Host(element, WrittenTo),
            "type" => element.Attributes.TryGetValue("stage", out var stage) && stage is string text ? text : "",
            _ => throw new CelException($"written() reads id, group, from, to or type, and not '{name}'."),
        };
    }

    private static bool IsLaterHolder(object? argument) =>
        argument is DislElement element
            ? element.HostAttributes.TryGetValue(LaterHolder, out var later) && later is true
            : throw new CelException("laterHolder() takes an entry of this diagram.");

    private static string Host(DislElement element, string name) =>
        element.HostAttributes.TryGetValue(name, out var value) && value is string text ? text : "";
}
