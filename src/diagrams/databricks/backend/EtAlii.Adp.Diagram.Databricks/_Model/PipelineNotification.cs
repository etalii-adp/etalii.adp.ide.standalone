using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>One <c>notifications:</c> entry - who hears about which pipeline events.</summary>
/// <param name="Recipients">The <c>email_recipients</c>, in file order.</param>
/// <param name="Alerts">The alert kinds the entry subscribes to, in file order.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record PipelineNotification(
    IReadOnlyList<string> Recipients,
    IReadOnlyList<string> Alerts,
    LineRange Lines);
