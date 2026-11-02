using System.Text;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Where a map's ids are kept, since the <c>.owm</c> has no place for them (Requirement 3.7), read and written through
/// <c>EtAlii.Adp.Specification.Fbl</c> (FBL 0.12 §8.6, §8.7, Q3): the <c>identities:</c> block of the map's registration,
/// or the legacy <c>{base}.identities.json</c> the tool wrote before.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which file.</b> The registration is the <c>{base}.adp</c> beside the body whose first line is the map's origin and
/// that names no other body. Its <c>identities:</c> block holds the ids when it has one; otherwise an existing
/// <c>{base}.identities.json</c> does, read and written in the shape it has, an array of <c>{"id", "kind", "key"}</c>
/// (FBL 0.12 §8.7); otherwise the map keeps them in the registration, whose <c>identities:</c> block its first save
/// writes (Q3), and a map that has no registration yet is given one, which names the map's origin and holds the block.
/// A legacy file is never created, as FBL 8.7 forbids for a new document. A registration beside the map that is not its
/// own is never written; such a map keeps its ids for as long as it is open.
/// </para>
/// <para>
/// <b>Written by splices</b>, as the library plans them: an entry whose key changed has its key replaced, a gone one is
/// pruned and a new one inserted, and every other byte of either file stays as it was. Nothing is written for a map that
/// was opened and not edited (Requirement 6.2).
/// </para>
/// <para>
/// <b>An optimisation, never a dependency</b>: a file that cannot be read gives no ids, so they are given again, and a
/// failed write keeps the edit and warns with the sentence the tool has always given (Requirement 6.5).
/// </para>
/// </remarks>
/// <param name="writer">Publishes the files.</param>
public sealed class WardleyIdentityFiles(IFileWriter writer)
{
    private static readonly ILogger _logger = Log.ForContext<WardleyIdentityFiles>();

    /// <summary>The legacy identities file beside <paramref name="bodyPath"/>, as the binding's <c>registration.legacyIdentities</c> names it.</summary>
    public static string LegacyPathFor(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        return LegacySidecar.PathFor(WardleyBody.Binding.LegacyIdentities ?? "{base}.identities.json", bodyPath);
    }

    /// <summary>The registration beside <paramref name="bodyPath"/>: <c>{base}.adp</c>.</summary>
    public static string RegistrationPathFor(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        return IoPath.Combine(IoPath.GetDirectoryName(IoPath.GetFullPath(bodyPath)) ?? "", IoPath.GetFileNameWithoutExtension(bodyPath) + ".adp");
    }

    /// <summary>
    /// The ids kept for <paramref name="bodyPath"/>, by <c>kind:key</c> as the binding looks them up; none when no file keeps
    /// any or the one that does cannot be read.
    /// </summary>
    public IReadOnlyDictionary<string, string> Read(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        try
        {
            var path = RegistrationPathFor(bodyPath);
            if (File.Exists(path) && Registration(File.ReadAllBytes(path), bodyPath) is { } registration && registration.Identities() is { } block) return block;
            var legacy = LegacyPathFor(bodyPath);
            return File.Exists(legacy) ? LegacySidecar.Open(File.ReadAllBytes(legacy)).Identities() : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read the identities beside {BodyPath}; giving them again", bodyPath);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Keeps <paramref name="entries"/> for <paramref name="bodyPath"/> in the file that keeps its ids, by the splices that
    /// make it hold them; the warning of a write that failed, else empty.
    /// </summary>
    public async Task<string> WriteAsync(string bodyPath, IReadOnlyList<WardleyIdentityEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            var path = RegistrationPathFor(bodyPath);
            var exists = File.Exists(path);
            // Whatever stands at the legacy file's path is that file, so one that cannot be written is said so.
            var legacy = LegacyPathFor(bodyPath);
            var legacyExists = File.Exists(legacy) || Directory.Exists(legacy);
            var registration = exists ? Registration(await File.ReadAllBytesAsync(path, cancellationToken), bodyPath) : null;
            if (registration is not null && (registration.Identities() is not null || !legacyExists))
            {
                await WriteRegistrationAsync(path, registration, entries, cancellationToken);
            }
            else if (legacyExists)
            {
                await WriteLegacyAsync(legacy, entries, cancellationToken);
            }
            else if (!exists && entries.Count > 0)
            {
                await WriteRegistrationAsync(path, New(), entries, cancellationToken);
            }

            return "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Reported rather than swallowed, though the edit still succeeds: failing it would cost the user work they can
            // see, while saying nothing let the loss pass unnoticed. The map is intact, and its elements are given new ids
            // underneath any selection still pointing at the old ones the next time it is opened.
            _logger.Warning(exception, "Could not write the identities beside {BodyPath}", bodyPath);
            return $"The element identities beside {IoPath.GetFileName(bodyPath)} could not be saved, so this map's elements may be given new ids the next time it is opened.";
        }
    }

    /// <summary>The registration <paramref name="bytes"/> hold when it is <paramref name="bodyPath"/>'s own, opened through the binding; null otherwise.</summary>
    private static OpenRegistration? Registration(byte[] bytes, string bodyPath)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var parsed = AdpRegistration.Parse(text);
        var ownsBody = parsed.Body is null || string.Equals(IoPath.GetFileName(parsed.Body), IoPath.GetFileName(bodyPath), StringComparison.OrdinalIgnoreCase);
        return parsed.MimeType == Diagram.WardleyMap.Origin.ToString() && ownsBody ? OpenRegistration.Open(bytes, WardleyBody.Binding) : null;
    }

    /// <summary>A registration of the map's origin with nothing else in it, as one the tool creates is written.</summary>
    private static OpenRegistration New() =>
        OpenRegistration.Open(Encoding.UTF8.GetBytes(AdpRegistration.Compose(Diagram.WardleyMap.Origin.ToString())), WardleyBody.Binding);

    /// <summary>The <c>identities:</c> block made to hold <paramref name="entries"/>: each stored under <c>kind:key</c>, the stale ones pruned (FBL §8.6).</summary>
    private async Task WriteRegistrationAsync(string path, OpenRegistration registration, IReadOnlyList<WardleyIdentityEntry> entries, CancellationToken cancellationToken)
    {
        var before = registration.Bytes;
        registration.KnownKeys = entries.Select(entry => WardleyBody.LookupKey(entry.Kind, entry.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (registration.Change(new ModelChange.Identify(WardleyBody.LookupKey(entry.Kind, entry.Key), entry.Id)) is PlanResult.Refused refused)
            {
                _logger.Warning("The registration beside a map refused the id of {Kind} {Key}: {Reason}", entry.Kind, entry.Key, refused.Reason);
            }
        }

        if (!registration.Bytes.AsSpan().SequenceEqual(before)) await writer.SaveAsync(path, registration.Bytes, cancellationToken);
    }

    /// <summary>The legacy identities file made to hold <paramref name="entries"/>, by json splices (FBL 0.12 §8.7).</summary>
    private async Task WriteLegacyAsync(string path, IReadOnlyList<WardleyIdentityEntry> entries, CancellationToken cancellationToken)
    {
        var file = LegacySidecar.Open(await File.ReadAllBytesAsync(path, cancellationToken));
        var wanted = entries.Select(entry => new LegacyIdentity(entry.Id, entry.Kind, entry.Key)).ToList();
        if (file.PlanIdentities(wanted, WardleyBody.Kinds) is not PlanResult.Planned { Edit: { Splices.Count: > 0 } edit })
        {
            return;
        }

        file.Apply(edit);
        await writer.SaveAsync(path, file.Bytes, cancellationToken);
    }
}
