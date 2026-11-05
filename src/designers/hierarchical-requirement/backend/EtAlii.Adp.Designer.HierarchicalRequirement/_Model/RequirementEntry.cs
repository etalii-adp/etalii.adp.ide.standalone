namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// An entry of the hierarchy: an item or a requirement, a bullet under the heading Requirements
/// (hierarchical-requirement.des, Item and Requirement).
/// </summary>
/// <param name="Id">
/// Its number, <c>ITEM-0004</c> or <c>REQ-0031</c>. A placeholder, <c>ITEM-NEW</c> or <c>REQ-NEW</c>, has the number it is
/// given in memory, written with the next edit (DESL 0.2 section 5.3.5); a duplicate keeps the number written.
/// </param>
/// <param name="ParentId">The item it is nested in, by its number, or null at the top level.</param>
/// <param name="Title">The text between the number and the trailing words; empty when nothing stands there.</param>
/// <param name="Description">The lines indented under the bullet, empty when there are none.</param>
/// <param name="Tags">The <c>#name</c> words, in their order.</param>
/// <param name="Collapsed">Whether the registration keeps it collapsed.</param>
/// <param name="Line">The line of its bullet, from 1.</param>
/// <param name="Placeholder">Whether the file holds a placeholder where its number goes.</param>
public abstract record RequirementEntry(string Id, string? ParentId, string Title, string Description, IReadOnlyList<string> Tags, bool Collapsed, int Line, bool Placeholder);
