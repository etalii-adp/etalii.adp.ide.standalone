using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>One element's authored position on a view, in canvas units.</summary>
public sealed record C4Position(double X, double Y);

/// <summary>
/// Where the positions a user arranged by hand are kept, as FBL 8.7 says and Q3 rules (c4-disl-fbl task 16,
/// Requirement 7): in the registration's <c>layout:</c> block once it has one; else in the <c>*.layout.json</c> the
/// module wrote before FBL, read and written by splices while it exists and never created; else in the registration,
/// whose block the first placement creates. A position is view state: none is ever written into the <c>.dsl</c>.
/// </summary>
/// <remarks>
/// Every write is answered with the bytes it replaced, so that its undo puts back exactly that file as it was - which
/// removes a first placement's entry from its own view only (finding B17) - and refuses when the file has changed since.
/// </remarks>
/// <param name="writer">Publishes the file a position is written to.</param>
public sealed class C4Positions(IFileWriter writer)
{
    private static readonly ILogger _logger = Log.ForContext<C4Positions>();

    /// <summary>The binding's <c>registration.legacyLayout</c>, which <c>C4PositionsTests</c> holds to <c>structurizr.fbl</c>.</summary>
    public const string LegacyLayout = "{base}.layout.json";

    /// <summary>The layout file the binding names for a body: <see cref="LegacyLayout"/> beside it.</summary>
    public static string LegacyPathFor(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        return LegacySidecar.PathFor(LegacyLayout, bodyPath);
    }

    /// <summary>
    /// The stored positions of <paramref name="viewKey"/>, each under the id of <paramref name="ids"/> its key names ignoring
    /// case (FBL 0.11 §8.7). Anything that cannot be read yields none: a diagram opens on the computed layout rather than not at
    /// all because its positions are unreadable.
    /// </summary>
    public IReadOnlyDictionary<string, C4Position> Read(string bodyPath, string? registrationPath, string viewKey, IReadOnlySet<string> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentNullException.ThrowIfNull(ids);

        if (RegistrationPositions(registrationPath) is { Count: > 0 } registered)
        {
            return registered;
        }

        var legacy = LegacyPathFor(bodyPath);
        if (!File.Exists(legacy))
        {
            return new Dictionary<string, C4Position>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return LegacySidecar.Open(File.ReadAllBytes(legacy)).Positions(viewKey, ids)
                .ToDictionary(pair => pair.Key, pair => new C4Position(pair.Value.X, pair.Value.Y), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Ignoring an unreadable layout file beside {BodyPath}", bodyPath);
            return new Dictionary<string, C4Position>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// What the layout file beside <paramref name="bodyPath"/> keeps for an element the body does not store: in each of
    /// <paramref name="viewKeys"/>, an entry whose key names none of <paramref name="stored"/>, which the next placement in
    /// that view removes (Q7, FBL 0.11 §8.7), and a second entry for one id. None when there is no layout file or it cannot
    /// be read.
    /// </summary>
    public static IReadOnlyList<(string ViewKey, Finding Finding)> Findings(string bodyPath, IEnumerable<string> viewKeys, IReadOnlySet<string> stored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKeys);
        ArgumentNullException.ThrowIfNull(stored);

        var legacy = LegacyPathFor(bodyPath);
        if (!File.Exists(legacy))
        {
            return [];
        }

        try
        {
            var sidecar = LegacySidecar.Open(File.ReadAllBytes(legacy));
            return [.. viewKeys.SelectMany(viewKey => sidecar.Findings(viewKey, stored, Path.GetFileName(legacy)).Select(finding => (viewKey, finding)))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Not reporting on an unreadable layout file beside {BodyPath}", bodyPath);
            return [];
        }
    }

    /// <summary>
    /// Stores one element's position on one view, the stale entries of that view - keys that name no id the body holds -
    /// removed in the same write (Q7). Answers the inverse that puts the file back, or null when nothing was written, and a
    /// sentence when the position could not be stored, which leaves the move in place (Requirement 7.4).
    /// </summary>
    public async Task<(RestoreC4LayoutCommand? Inverse, string Warning)> StoreAsync(string bodyPath, string? registrationPath, string viewKey, string elementId, double x, double y, IReadOnlySet<string> stored, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentNullException.ThrowIfNull(stored);

        var legacy = LegacyPathFor(bodyPath);
        var inRegistration = registrationPath is { Length: > 0 } && File.Exists(registrationPath) && (RegistrationPositions(registrationPath) is { Count: > 0 } || !File.Exists(legacy));
        var path = inRegistration ? registrationPath! : legacy;
        if (!inRegistration && !File.Exists(legacy))
        {
            // No registration and no layout file: FBL creates no sidecar, so there is nowhere to keep it.
            return (null, NotSaved(bodyPath));
        }

        byte[] before;
        try
        {
            before = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Not updating the positions of {BodyPath}: {Path} could not be read right now", bodyPath, path);
            return (null, Unreadable(bodyPath));
        }

        var planned = inRegistration ? PlaceInRegistration(before, elementId, x, y, stored) : PlaceInLegacy(before, viewKey, elementId, x, y, stored);
        if (planned is null)
        {
            return (null, Unreadable(bodyPath));
        }

        if (planned.AsSpan().SequenceEqual(before))
        {
            return (null, "");
        }

        try
        {
            await writer.SaveAsync(path, planned, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not write the positions of {BodyPath} to {Path}", bodyPath, path);
            return (null, NotSaved(bodyPath));
        }

        return (new RestoreC4LayoutCommand(bodyPath, path, before, planned), "");
    }

    /// <summary>The registration's <c>layout:</c> block, or null when there is no registration to read.</summary>
    private static Dictionary<string, C4Position>? RegistrationPositions(string? registrationPath) =>
        registrationPath is { Length: > 0 } && File.Exists(registrationPath)
            ? RegistrationLayout.Read(registrationPath).ToDictionary(pair => pair.Key, pair => new C4Position(pair.Value.X, pair.Value.Y), StringComparer.OrdinalIgnoreCase)
            : null;

    private static byte[]? PlaceInLegacy(byte[] bytes, string viewKey, string elementId, double x, double y, IReadOnlySet<string> stored)
    {
        var sidecar = LegacySidecar.Open(bytes);
        return sidecar.Change(file => file.PlanPlace(viewKey, elementId, x, y, stored)) is PlanResult.Planned ? sidecar.Bytes : null;
    }

    private static byte[]? PlaceInRegistration(byte[] bytes, string elementId, double x, double y, IReadOnlySet<string> stored)
    {
        var registration = OpenRegistration.Open(bytes);
        registration.KnownIds = stored;
        return registration.Change(new ModelChange.Place(elementId, x, y)) is PlanResult.Planned ? registration.Bytes : null;
    }

    /// <summary>What a move says when the file its position goes to could not be read.</summary>
    internal static string Unreadable(string bodyPath) =>
        $"The layout beside {Path.GetFileName(bodyPath)} could not be updated, so positions may not be as expected when the diagram is reopened.";

    /// <summary>What a move says when its position could not be written.</summary>
    internal static string NotSaved(string bodyPath) =>
        $"The new position could not be saved beside {Path.GetFileName(bodyPath)}, so it will not be there when the diagram is reopened.";
}
