namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// Today's code reproduces the frozen transcript byte for byte: the oracle every step of the conversion to a
/// DISL definition and an FBL binding is held to (azure-devops-pipeline-disl-fbl Requirement 1.3).
/// </summary>
/// <remarks>
/// A difference is either a regression or a change that specification names. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the diff of the
/// checked-in file is then the review of that change (Requirement 1.4).
/// </remarks>
public sealed class PipelineTranscriptTests : TranscriptTests<PipelineTranscript>;
