# Requirements Document

## Introduction

This spec produces a static, purely visual mockup of the diagram IDE's workspace shell — the layout [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) Requirement 1 already calls for (activity/command area, panels, tabbed editor area), given concrete visual shape here for the first time. It fills the exact placeholder `App.tsx`'s `Gate()` already renders once a project is opened ("Workspace shell for ... goes here (adp-diagram-ide)"), replacing it with the shell's actual chrome: a command-buttons ribbon, a Hierarchy panel, a Diagram tab pane, a Property Grid panel, an Errors & Warnings panel, a Toolbox pane, and a Search pane — each hosted as a tab within a resizable, dockable pane, with more than one tab sharing a pane in the default layout to prove the mechanism works before any real panel content is built on top of it.

Every panel's inner content is an explicit, clearly-marked placeholder. This spec implements no diagram rendering, no file/folder listing, no property editing, no error/warning collection, and no backend/gRPC integration of any kind — those remain entirely the responsibility of the specs that already own, or will later own, that content (e.g. [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md) for the Hierarchy panel's real content).

## Alignment with Product Vision

- **"A familiar surface"** ([product.md](../../steering/product.md)): giving the shell concrete visual shape early, in a classic IDE layout (a top command ribbon, dockable tool-window-style panes, a tabbed document area), is exactly the "close to VS Code" experience product.md calls for, drawn out before any panel's real behavior exists to distract from judging the layout itself.
- **"Don't reinvent, integrate"** (product.md): the panel set (Hierarchy, Toolbox, Search, Property Grid, Errors & Warnings) and the dockable-tab-pane mechanism mirror conventions already established by mainstream IDEs (VS Code's side panels and editor tabs, Visual Studio's dockable tool windows) rather than inventing a novel layout.
- [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) **Requirement 1 (Workspace shell)**: this spec is the first concrete realization of that requirement's "activity bar, (file) explorer, tabbed editor area, status bar," extended with the additional panels (ribbon, Property Grid, Errors & Warnings, Toolbox, Search) this spec introduces for the first time.
- [tech.md](../../steering/tech.md)'s **Client Technology Stack**: the mockup is built with the same React + TypeScript client already established, not a separate prototyping tool, and reuses the existing `@mdi/font` icon integration rather than adding one.

## Requirements

### Requirement 1 — Mockup fills the existing workspace-shell hand-off point

**User Story:** As a developer building EtAlii.Adp, I want a concrete visual mockup of the diagram IDE's workspace shell, so that the overall layout can be reviewed and agreed on before any panel's real functionality is built.

#### Acceptance Criteria

1. WHEN a user opens a project from the project grid THEN the system SHALL render this mockup in place of the placeholder currently shown by `App.tsx`'s `Gate()` ("Workspace shell for ... goes here (adp-diagram-ide)"), consistent with `login-project-selection` Requirement 2.3's hand-off point.
2. WHEN the mockup is rendered THEN the system SHALL NOT perform any gRPC call, read any project file, or otherwise depend on backend data — every panel's content is static placeholder content, independent of which project was opened.
3. WHEN the user returns to the project grid (the existing "Back to projects" action) THEN the system SHALL unmount the mockup the same way the current placeholder text already does, with no new behavior required beyond what `Gate()` already provides.
4. IF the browser window is resized THEN the system SHALL reflow the mockup's layout responsively without losing pane sizes or the active tab in any pane, consistent with `adp-diagram-ide` Requirement 1.4.

### Requirement 2 — Command buttons ribbon

**User Story:** As a diagram author, I want a ribbon of command buttons at the top of the workspace, so that common actions have a familiar, always-visible home.

#### Acceptance Criteria

1. WHEN the mockup renders THEN the system SHALL display a horizontal ribbon docked to the top of the workspace, above the panes, spanning the full width.
2. WHEN the ribbon renders THEN the system SHALL populate it with a representative set of placeholder command buttons (icon + label, using the existing Material Design Icons integration) grouped logically (e.g. file actions, edit actions, view actions), so the ribbon's intended density and grouping can be reviewed even though no button performs a real action yet.
3. WHEN a placeholder ribbon button is activated THEN the system SHALL NOT perform any real action — at most a purely visual pressed/active state — and the system SHALL mark that spot with a code comment identifying it as where a real command handler will be wired in later.

### Requirement 3 — Dockable, tabbed pane mechanism

**User Story:** As a diagram author, I want panels to behave like tabs arranged into panes, so that I can organize my workspace the way I already do in familiar IDEs.

#### Acceptance Criteria

1. WHEN the mockup renders THEN the system SHALL organize its panel content (Hierarchy, Diagram, Property Grid, Errors & Warnings, Toolbox, Search) into one or more resizable panes, where each pane hosts one or more tabs.
2. WHEN a pane hosts more than one tab THEN the system SHALL display a tab strip for that pane and SHALL show only the active tab's content at a time.
3. WHEN the user selects a different tab within a pane THEN the system SHALL switch that pane's visible content to the selected tab without affecting any other pane's active tab.
4. WHEN the user drags a pane's border THEN the system SHALL resize the adjacent panes accordingly, within reasonable minimum-size bounds so no pane can be resized away entirely.
5. This spec SHALL NOT require drag-and-drop rearrangement of tabs between panes, or floating/undocked panes — the mockup demonstrates a fixed, reviewable default arrangement (Requirement 4), not a fully general docking editor; broader rearrangement remains a candidate for a future spec if reviewers want it after seeing this mockup.

### Requirement 4 — Default panel placement demonstrates the pane/tab mechanism

**User Story:** As a reviewer, I want to see a sensible default arrangement of all six panels, so that I can judge whether the proposed layout actually works before it's built for real.

#### Acceptance Criteria

1. WHEN the mockup first renders THEN the system SHALL place the Hierarchy panel as a tab in a pane on one side of the workspace.
2. WHEN the mockup first renders THEN the system SHALL place the Diagram panel as a tab in the main, central pane, and SHALL include at least one additional placeholder diagram tab in that same pane, demonstrating multiple documents open at once (per `adp-diagram-ide` Requirement 1.2's "new editor tab" behavior this mockup visually anticipates).
3. WHEN the mockup first renders THEN the system SHALL place the Property Grid and Errors & Warnings panels as two tabs sharing one pane, demonstrating a pane holding more than one distinct panel type.
4. WHEN the mockup first renders THEN the system SHALL place the Toolbox and Search panels as tabs — either sharing a pane with the Hierarchy panel or in a pane of their own — demonstrating that a non-editor-area pane can also hold multiple distinct panel types as tabs.
5. This default arrangement is illustrative, not prescriptive: Requirement 3 SHALL keep the pane/tab mechanism generic enough that a different default arrangement can be substituted later without changing how panes or tabs themselves behave.

### Requirement 5 — Placeholder content contract

**User Story:** As a developer picking this work up later, I want every panel's inner content to obviously be a placeholder, so that I know exactly what still needs to be built and never mistake the mockup for finished functionality.

#### Acceptance Criteria

1. WHEN any of the six panels (Hierarchy, Diagram, Property Grid, Errors & Warnings, Toolbox, Search) renders its content area THEN the system SHALL show a clearly visible placeholder — at minimum the panel's name and a short description of what will eventually live there — rather than an empty area.
2. WHEN a panel's placeholder is implemented in code THEN the system SHALL mark that exact spot with a code comment identifying which future spec is expected to replace it (e.g. referencing `project-root-folder-explorer` at the Hierarchy panel's content), mirroring the existing hand-off comment already in `App.tsx`'s `Gate()`.
3. This spec SHALL NOT implement any of: real file/folder listing, diagram rendering or editing, property editing, error/warning collection, toolbox item drag-and-drop, or search execution — every one of those remains entirely the responsibility of the spec that owns that capability.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: the dockable-pane/tab mechanism (Requirement 3) SHALL be implemented as a component that has no knowledge of what content any given tab hosts, so it can be reused unchanged once real panel content replaces today's placeholders.
- **Modular Design**: each of the six panel placeholders SHALL be its own component, so replacing one panel's placeholder with real content later touches only that file, not the pane/tab mechanism or the other five placeholders.

### Performance

- Being static placeholder content with no backend calls, the mockup SHALL render and become interactive (pane resize, tab switch) with no noticeable delay.

### Security

- No new surface: the mockup only renders behind `AuthContext`'s existing authenticated gate (`login-project-selection` Requirement 1) and performs no backend calls of its own (Requirement 1.2).

### Reliability

- Not applicable beyond ordinary rendering: a static mockup with no backend dependency (Requirement 1.2) has no failure modes of its own to define error handling for.

### Usability

- The mockup SHOULD look and feel close enough to a finished IDE (spacing, consistent icon usage via `@mdi/font`, centralized styling per tech.md's Frontend section) that reviewers can judge the real layout from it, not just a rough wireframe.
