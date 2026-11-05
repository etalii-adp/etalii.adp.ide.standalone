namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The module reproduces the frozen transcript byte for byte: the oracle every step of the
/// conversion to the DISL definition and the FBL binding is held to, frozen from the hand-written
/// code before any of it was changed (ansible-structure-disl-fbl, Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change, and this conversion names none by
/// default (Q2 to Q4). Only an intended change is fixed by regenerating -
/// <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the diff of the
/// checked-in file is then the review of that change (Requirement 1.4).
/// </remarks>
public sealed class AnsibleTranscriptTests : TranscriptTests<AnsibleTranscript>;
