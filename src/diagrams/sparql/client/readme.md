# sparql client

## Inline renaming

**Exempt, not pending: there is no rename to mark.** This module offers no context actions at
all - its provider discovers none and refuses every execution - so no prompt exists to mark. A
variable's name on screen is the query's own text; changing it is a text edit with scoping
rules, not a label swap. If a rename action is ever added, it adopts the ordinary way through
the shared label library (`src/client/src/canvas/label/readme.md`).
