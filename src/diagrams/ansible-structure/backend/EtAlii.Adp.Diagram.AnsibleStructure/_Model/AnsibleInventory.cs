namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>One inventory - an environment under <c>inventories/</c>, or a root inventory file.</summary>
/// <param name="Name">The environment folder's name, or the file's name for a root inventory.</param>
/// <param name="RelativePath">The folder or file, relative to the diagram's folder.</param>
/// <param name="Groups">The groups it defines, with their host counts.</param>
/// <param name="VariableFolders">Its <c>group_vars</c> and <c>host_vars</c>, when it has them.</param>
public sealed record AnsibleInventory(
    string Name,
    string RelativePath,
    IReadOnlyList<AnsibleInventoryGroup> Groups,
    IReadOnlyList<AnsibleVariableFolder> VariableFolders);
