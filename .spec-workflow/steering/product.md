# A Different Perspective

In the current era where AI is starting to dominate the software development landscape, tools that facilitate architecture support are also finding themselves on a crossroad. Large (enterprise) architecture solutions surely facilitate strong, strict principles and guidelines, but at the same time also slow down the pace of development making them less competitive towards the many AI driven development solutions.

The ADP (acronym for "A Different Perspective") tries to change this by making it easy by combining many of the different architectural views in ways that makes sense from the chosen development aproach.   &#x20;

For this the following mantra will be followed:

* Architectural artifacts are intended to be part of the solution repositories, and where applicable hence also adhere to the applied version control principles.
* Architectural artifacts on their own do not make sense. Therefore one focus will be to find out where and how linkage between individual architectural and code artifacts can add value.&#x20;
* There is no need to reinvent the wheel. This means that well-known visualizations, file formats and principles will be embraced and enhanced to fit in the above. &#x20;
* The ADP toolset should be be easily incorporated in existing solutions and code repositories, and also in a minimal form already begin to add value.
* AI capabilities will be embraced, but not as the core foundation - ADP is intended to smoothen the architectural discussions with AI tools.

## Target users

* Developers and architects who already live inside a code repository and don't want to leave it (or switch to a heavyweight, disconnected modelling tool) to reason about or record architecture.
* Teams who want architectural artifacts to travel with the code they describe - reviewed in the same pull requests, branched and merged with the same version control history.
* Small teams and individual contributors first: ADP should already add value when dropped into a single repository, with no organization-wide rollout required.

## Key capabilities

1. **Diagramming on files**: view and edit a wide range of architectural/visual diagram types, each backed by plain, diffable files rather than a proprietary database.
2. **Linkage over illustration**: diagrams are not just pictures - individual diagram elements can be linked to the code artifacts they represent, so the diagram stays meaningful as the code evolves.
3. **A familiar surface**: a web-based editing experience with a layout and interaction model close to VS Code, so adopting ADP doesn't mean learning a new tool from scratch.
4. **Live, pushed updates**: changes to the underlying files (from ADP itself, another editor, or version control operations) are reflected in open views without manual refreshing.
5. **Minimal footprint, incremental value**: usable standalone and locally with no required backend infrastructure beyond the workspace's own files; more advanced/hosted scenarios are additive, not prerequisites.

## Product principles (elaborated)

* **Files are the source of truth.** No diagram should be able to exist in a form that can't be reviewed as a diff, checked out, or branched like any other repository artifact.
* **Value from day one.** A team should get something useful from ADP after adding it to one repository, before any wider adoption decision is made.
* **Don't reinvent, integrate.** Prefer embracing established visual notations, file formats, and editor conventions (like VS Code's UX) over inventing new ones, unless there's a clear reason.
* **AI as an accelerant, not the core.** ADP's job is to make architecture legible to both humans and AI tools discussing a codebase - not to autonomously generate or own the architecture itself.

## Future vision

* Start standalone and local-only; later support hosting the backend elsewhere to enable shared, real-time architectural discussions across a team.
* Progress from a fully separate web client towards partial embedding inside VS Code itself, so ADP can live alongside the code it describes rather than in a second window.