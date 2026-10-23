namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// A question, remark, warning or idea about one entry, a bullet under the heading Assessments (hierarchical-requirement.des,
/// Assessment). It has no number: its id is its place among the document's assessments, <c>assessment/0</c> onwards.
/// </summary>
/// <param name="About">The number of the entry it is about, as written.</param>
/// <param name="Detail">The lines indented under it, empty when there are none.</param>
/// <param name="Resolved">Whether its checkbox is ticked; one without a checkbox is open.</param>
public sealed record Assessment(string Id, AssessmentKind Kind, string About, string Text, string Detail, bool Resolved, int Line);
