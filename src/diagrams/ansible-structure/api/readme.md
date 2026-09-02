# Ansible structure — api

`.proto` extensions specific to the Ansible structure diagram type: the payload one node or
edge carries, packed into the core `Element`'s `Any`. No core proto is edited from here.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`ansible-structure-diagram`](../../../../.spec-workflow/archive/specs/ansible-structure-diagram/) for
this diagram type's spec.

## Two things about this proto worth knowing before editing it

**It generates into `EtAlii.Adp.Diagram.AnsibleStructure.Wire`, not the module's own namespace.**
Every other module generates into its own. This one cannot: its wire shapes and its domain model
share four names — `AnsibleEdge`, `AnsibleEdgeKind`, `AnsibleRoleContents`,
`AnsibleInventoryGroup` — because they are genuinely the same concepts, flattened for the wire.
Generating into the module namespace is a hard `CS0101` collision with the `_Model` records.
Renaming the wire types to dodge it would leave every reader guessing which
`AnsibleEdgeMessage` belonged to what; the sub-namespace says it once, and every use site then
reads `Wire.AnsibleEdge` versus `AnsibleEdge` with nothing left to guess.

**`play_index` is an index, never a colour.** Requirement 6.3 wants a play's roles tied to it
visually, and it would be easy to put a colour on the wire. Don't: the palette belongs in the
module's stylesheet per tech.md's centralised-styling rule, and a colour here would put half the
design in the backend and turn a theme change into a protocol change.
