# A Different Perspective

Some things are understood better as a picture than as prose, and better as a picture made for that one task than as a general-purpose one. A causal loop explains a feedback system in a way a paragraph cannot; a Wardley map says what a list of components does not; a structured view of a long text shows what reading it top to bottom hides. General-purpose drawing tools can produce any of these, but they know nothing about what is being drawn, and so they help with none of it.

ADP — "A Different Perspective" — is a family of **specialized diagram, designer and editor experiences**, each tuned to one task, for every situation in which a specialized visualization serves better than a generalized one or a textual description. Architecture is one such situation and ADP began there, but it is not the scope. The designers now being introduced target, among others:

* **(Constructive) technology assessment** — structuring the actors, effects, scenarios and uncertainties of a technology under discussion.
* **Collaboration between humans and agents** — making plans, hand-offs, responsibilities and progress legible to both.
* **Bringing clarity to textual data** — giving long or dense text a shape that can be navigated, questioned and corrected.

For this the following mantra is followed:

* **Files are the source of truth.** What a designer shows lives in plain, diffable files beside the work it describes, under the same version control, reviewed in the same pull requests.
* **Specialized over generalized.** A designer earns its place by knowing its subject: its element types, what may connect to what, and what a well-formed picture looks like. A designer that could draw anything is not the goal.
* **Don't reinvent the wheel.** Where a well-known notation, file format or convention exists, it is embraced rather than replaced, and a file stays openable by the tool that owns its format.
* **Defined, not coded.** Every diagram, designer and editor is described by a markup-language *definition*. Code is written only where a definition cannot express what is needed.
* **Live where the user already works.** ADP comes to the user's IDE rather than asking the user to come to ADP.
* **Value from day one.** Useful in a single repository, before any wider adoption decision.
* **AI as an accelerant, not the core.** ADP makes structure legible to humans and to the agents working beside them; it does not generate or own that structure itself.

## Definitions and hosts

ADP is not one application. It is a set of **definitions** and a set of **hosts** that interpret them.

* **Definitions** say what a diagram, designer or editor *is*: its element and connection types, how each looks, what the toolbox offers, which constraints hold, how it is laid out and how it is persisted. Their specification — the formats and languages a definition is written in, such as DEDL, the Diagram Editor Definition Language — lives in the [`etalii.adp`](https://github.com/etalii-adp/etalii.adp) repository, which is the single source of truth every host implements.
* **Hosts** are core diagram and editor plugins, one per IDE, each in its own repository: `etalii.adp.ide.standalone` (this repository, a web workspace), `etalii.adp.ide.intellij`, `etalii.adp.ide.vscode` and `etalii.adp.ide.eclipse`. A host interprets a definition; when a definition alone is not enough, the host's plugin provides the additional code that supports it.

No host is the reference implementation. This repository's technical shape — an ASP.NET Core backend, a React client, gRPC between them, diagram modules under `src/diagrams/` — is how *this* host works, not the standard the others follow.

## Target users

* People who reason about something that is clearer drawn than written — developers and architects, but equally analysts, assessors, researchers and teams coordinating with agents.
* Teams who want those drawings to travel with the work they describe — reviewed in the same pull requests, branched and merged with the same history.
* Authors of new designers, who describe a designer in a definition once and get it in every supported IDE.
* Small teams and individual contributors first: ADP should add value when dropped into a single repository, with no organization-wide rollout required.

## Key capabilities

1. **Specialized designers on files**: view and edit task-specific diagrams and text views, each backed by plain, diffable files rather than a proprietary database.
2. **Linkage over illustration**: elements can be linked to the artifacts they represent — code, documents, other diagrams — so a picture stays meaningful as its subject evolves.
3. **Definition-driven**: a new designer is a definition first, interpreted by every host, with host-specific code only where the definition runs out.
4. **In the user's own IDE**: the same designers in IntelliJ Platform IDEs, VS Code, Eclipse and a standalone web workspace.
5. **Live, pushed updates**: changes to the underlying files — from ADP itself, another editor, or version control — reach open views without refreshing.

## Future vision

* Grow the catalog of specialized designers, prioritizing technology assessment, human–agent collaboration and textual clarity alongside the architecture notations already supported.
* Bring the definition specification to a stable version that every host conforms to, so a designer written once opens identically everywhere.
* Support shared, real-time sessions across a team, as an additive capability rather than a prerequisite.
