# Wardley Map — client

TypeScript and related code for the web client's wardley map view.

Origin: `wardley/map`. See [../../readme.md](../../readme.md) for what this folder is for.

**Links, flow links and evolve indicators are not selectable, by decision.** A link is the value
chain's dependency between two components and an evolve indicator says where one is heading:
both are read, not picked, so their relation types declare `selectable: false` and a press on one
is a press on the background. The same goes for the far end of an evolve indicator. These are
decisions about the notation, not omissions; making any of them selectable is a separate question
for the user ([centralized-selection](../../../../.spec-workflow/specs/centralized-selection/requirements.md),
Requirements 2.3 and 2.4). Selection and its look are the canvas library's.
