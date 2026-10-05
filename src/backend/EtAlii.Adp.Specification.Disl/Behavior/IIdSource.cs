using System.Numerics;
using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Where the id of an element a <c>create</c> makes comes from (DISL §11.5.1): the specification's
/// <c>persistence.ids</c> strategy for a runtime, a fixed list for a test or for a host that minted
/// the id already, so that a redone add keeps it.
/// </summary>
public interface IIdSource
{
    /// <summary>The id of the next element of <paramref name="type"/>.</summary>
    string Next(string type);
}

/// <summary>The id sources this runtime has.</summary>
public static class DislIds
{
    /// <summary>A source that gives <paramref name="ids"/> in order, and refuses a create past the last.</summary>
    public static IIdSource Fixed(params string[] ids) => new FixedIds(ids);

    /// <summary>
    /// The specification's own strategy (§11.5.1), for the strategies a runtime can mint without the
    /// model: <c>uuid-v4</c> and <c>uuid-v7</c>, in <c>hex</c>, <c>base64url</c> or <c>base36</c>.
    /// </summary>
    public static IIdSource Of(DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var ids = specification.Root.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("ids", out var declared) && declared.ValueKind == JsonValueKind.Object
            ? declared
            : default;
        var strategy = ids.ValueKind == JsonValueKind.Object ? DislJson.String(ids, "strategy") ?? "uuid-v7" : "uuid-v7";
        var encoding = ids.ValueKind == JsonValueKind.Object ? DislJson.String(ids, "encoding") ?? "hex" : "hex";
        return strategy switch
        {
            "uuid-v4" => new Uuids(Guid.NewGuid, encoding),
            "uuid-v7" => new Uuids(Guid.CreateVersion7, encoding),
            _ => throw new NotSupportedException($"This runtime cannot mint ids of strategy '{strategy}' without the model; give the interpreter an id source."),
        };
    }

    private sealed class FixedIds(IReadOnlyList<string> ids) : IIdSource
    {
        private int _next;

        public string Next(string type) =>
            _next < ids.Count ? ids[_next++] : throw new InvalidOperationException($"No id is left for a new {type}.");
    }

    private sealed class Uuids(Func<Guid> mint, string encoding) : IIdSource
    {
        public string Next(string type)
        {
            var guid = mint();
            return encoding switch
            {
                "base36" => Base36(guid),
                "base64url" => Convert.ToBase64String(guid.ToByteArray(bigEndian: true)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                _ => guid.ToString("D"),
            };
        }

        /// <summary>The 128-bit value as an unsigned integer in lowercase base 36, left-padded with 0 to 25 characters (§11.5.1).</summary>
        private static string Base36(Guid guid)
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            var value = new BigInteger(guid.ToByteArray(bigEndian: true), isUnsigned: true, isBigEndian: true);
            var text = new char[25];
            for (var index = text.Length - 1; index >= 0; index--)
            {
                text[index] = alphabet[(int)(value % 36)];
                value /= 36;
            }
            return new string(text);
        }
    }
}
