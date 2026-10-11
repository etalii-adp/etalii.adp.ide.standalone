using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One knowledge file's body, read through FBL with the binding its extension selects. The module
/// has no parser and no writer of its own: reading is the library's reading of the bundled
/// binding's rules, so the three formats give one model, and a file that was not changed is the
/// bytes it was read from.
/// </summary>
internal sealed class KnowledgeBody
{
    /// <summary>
    /// What is said of a file that does not open with the mark of the format version the bundled
    /// bindings read. The bindings require that mark, so such a file is not read at all: a file of
    /// a later version may hold what this version would misread, and a misread file that is then
    /// written is a damaged file.
    /// </summary>
    public const string AnotherVersion =
        "This file does not say it is a knowledge file of version 0.1, the version this application knows. It is left exactly as it is and cannot be changed here.";

    private readonly OpenBody? _body;

    private KnowledgeBody(byte[] bytes, OpenBody? body, FblModel model, string unreadable)
    {
        Bytes = bytes;
        _body = body;
        Model = model;
        Unreadable = unreadable;
        Table = unreadable.Length > 0 ? KnowledgeTable.Empty : KnowledgeModelReader.Read(model);
    }

    /// <summary>The bytes the body was read from.</summary>
    public byte[] Bytes { get; }

    /// <summary>What the reading gave: the elements, and what it has to report.</summary>
    public FblModel Model { get; }

    /// <summary>The table the elements make. Empty for a body that cannot be read.</summary>
    public KnowledgeTable Table { get; }

    /// <summary>
    /// Why the body cannot be read at all, with where - the format's own error and its position -
    /// or empty when it can. Such a body is shown as its finding and is never written.
    /// </summary>
    public string Unreadable { get; }

    /// <summary>Why the table cannot be edited, or empty when it can: it cannot be read, or its binding says it is read-only.</summary>
    public string ReadOnlyReason
    {
        get
        {
            if (Unreadable.Length > 0)
            {
                return Unreadable;
            }

            return _body is { IsReadOnly: true } ? "This file cannot be changed here." : "";
        }
    }

    /// <summary>
    /// The body's bytes with <paramref name="changes"/> made, or why they cannot be. The changes are
    /// made to a copy, one after the other, each planned against the result of the one before; the
    /// first that is refused ends it, and this body is as it was either way.
    /// </summary>
    public (byte[]? Bytes, string Refusal) Change(IReadOnlyList<ModelChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (_body is null || ReadOnlyReason.Length > 0)
        {
            return (null, ReadOnlyReason.Length > 0 ? ReadOnlyReason : "This file cannot be changed here.");
        }

        var copy = _body.Fork();
        foreach (var change in changes)
        {
            if (copy.Change(change) is PlanResult.Refused refused)
            {
                return (null, refused.Reason);
            }
        }

        return (copy.Bytes, "");
    }

    /// <summary>
    /// Reads a body. Never throws on content: a file that is not well-formed, or whose extension no
    /// binding reads, comes back with <see cref="Unreadable"/> saying why.
    /// </summary>
    /// <param name="bytes">The file's bytes, exactly as they are on disk.</param>
    /// <param name="fileName">The file's name, for its extension and for the findings.</param>
    public static KnowledgeBody Read(byte[] bytes, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName);
        if (KnowledgeDefinition.BindingFor(extension) is not { } binding)
        {
            return new KnowledgeBody(bytes, null, new FblModel([], [], Unreadable: true), $"A knowledge file is YAML, JSON or XML; '{extension}' is none of them.");
        }

        var body = OpenBody.Open(bytes, binding, new FblOptions
        {
            FileName = Path.GetFileName(fileName),
            DeriveId = KnowledgeDefinition.DeriveId,
            AttributeType = KnowledgeDefinition.AttributeType,
            ReportUnboundKeys = true,
        });
        var model = body.Model;
        var unreadable = "";
        if (model.Unreadable)
        {
            var finding = model.Findings.FirstOrDefault(candidate => candidate.Severity == FindingSeverity.Error) ?? model.Findings.FirstOrDefault();
            unreadable = finding is null
                ? "This file cannot be read."
                : finding.Message.StartsWith("The body does not start with the mark", StringComparison.Ordinal)
                    ? AnotherVersion
                : finding.Location is { } at
                    ? $"This file cannot be read: {finding.Message} (line {at.Line}, column {at.Column})"
                    : $"This file cannot be read: {finding.Message}";
        }

        return new KnowledgeBody(bytes, body, model, unreadable);
    }
}
