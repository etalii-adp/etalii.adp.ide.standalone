# Requirements Document

## Introduction

This spec adds a reusable, content-agnostic `ContextMenu` component to the web client — a right-click (and keyboard-accessible) menu with icons, nested submenus, and disabled items, whose content is *composable*: independent parts of the app can each contribute their own group of menu items, merged into one menu at render time without those contributors needing to know about each other. It is a UI primitive in the same spirit as `diagram-ide-mockup`'s `SplitPane`/`TabbedPane` — generic layout/interaction mechanics with zero knowledge of what any specific consumer puts in it.

This spec delivers the mechanism only. It does not add a context menu to any specific surface (the explorer tree, the diagram canvas, the ribbon, etc.) or define what any menu item does — those are each a future consumer's concern, wiring `ContextMenu` in with their own items. The nearest known consumer is [`rename-files-and-folders`](../rename-files-and-folders/requirements.md), whose Usability guidance already names "a context-menu 'Rename' action" as the expected trigger convention; that spec can adopt this component once it exists, but doing so is out of scope here.

## Alignment with Product Vision

- **"A familiar surface"** ([product.md](../../steering/product.md)): a right-click menu with icons and submenus is a baseline expectation of any VS Code-like or IDE-like surface; `adp-diagram-ide`'s usability goal and `diagram-ide-mockup`'s "familiar IDE conventions" guidance both point at this same expectation.
- **"Don't reinvent, integrate"** (product.md): reuses the existing `@mdi/font` icon integration for item icons rather than adding an icon package, and reuses `index.css`'s existing themed CSS custom properties rather than a component-local palette — the same reuse pattern `diagram-ide-mockup` already established for `SplitPane`/`TabbedPane`/`RibbonBar`.
- **"Modular Design"** (`adp-diagram-ide` Non-Functional Requirements) / structure.md's core-vs-diagram-type-plugin boundary: composability is what lets a future diagram-type module contribute its own context-menu items for its own elements without core code needing to know about that diagram type, mirroring the same dependency direction already required elsewhere (diagram-type-specific code may depend on core abstractions; core must never depend on a specific diagram type).
- [`rename-files-and-folders`](../rename-files-and-folders/requirements.md)'s Usability guidance: "Triggering rename SHOULD follow familiar IDE conventions (e.g. a context-menu 'Rename' action, and/or the F2 keyboard shortcut)" — this spec is what would eventually let that spec deliver on the context-menu half of that guidance.

## Requirements

### Requirement 1 — Rendering a context menu

**User Story:** As a user, I want to right-click something and see a menu of relevant actions, so that I can act on it without hunting through a ribbon or toolbar.

#### Acceptance Criteria

1. WHEN a consumer renders `ContextMenu` in an open state at a given screen position THEN the system SHALL display a floating menu of items anchored at that position, above other page content.
2. WHEN the open `ContextMenu` would render partially outside the browser viewport THEN the system SHALL reposition it (e.g. flip horizontally/vertically) so the entire menu remains visible.
3. WHEN the user clicks outside the open menu, presses `Escape`, or selects a non-disabled item THEN the system SHALL close the menu, notifying the consumer via a callback so it can clear whatever state opened it.
4. WHEN the user selects a non-disabled item (by click or by keyboard activation) THEN the system SHALL invoke that item's own action callback and then close the menu (Requirement 1.3), without the menu needing any awareness of what that action does.
5. This spec SHALL NOT define how or where any consumer opens the menu (right-click handler placement, position calculation) beyond providing the position it's told to render at — that remains each consumer's own responsibility.

### Requirement 2 — Item icons

**User Story:** As a user, I want menu items to show a familiar icon next to their label, so that I can scan the menu quickly instead of reading every label.

#### Acceptance Criteria

1. WHEN a menu item definition includes an icon THEN the system SHALL render it using the existing Material Design Icons integration (`@mdi/font`, `mdi mdi-*` classes), consistent with every other icon usage already established in the client (Alignment with Product Vision).
2. WHEN a menu item definition omits an icon THEN the system SHALL still align its label consistently with icon-bearing sibling items (e.g. reserved icon-width space), so a menu mixing icon and non-icon items doesn't look misaligned.
3. This spec SHALL NOT introduce per-item custom icon rendering (SVGs, images) beyond the `@mdi/font` class-name mechanism already used everywhere else in the client.

### Requirement 3 — Nested submenus

**User Story:** As a user, I want related actions grouped under a submenu, so that a long list of actions doesn't overwhelm the top-level menu.

#### Acceptance Criteria

1. WHEN a menu item definition includes its own nested items THEN the system SHALL render that item with a visual indicator (e.g. a trailing chevron) that it opens a submenu, rather than performing an action itself when selected.
2. WHEN the user hovers over, or keyboard-activates, an item with nested items THEN the system SHALL open its submenu adjacent to that item, positioned to stay within the viewport (Requirement 1.2 applies at every nesting level).
3. WHEN the user moves away from an open submenu without selecting one of its items or opening a different submenu THEN the system SHALL close that submenu, without closing the top-level menu.
4. Nesting SHALL be supported to at least two levels deep (a submenu item may itself open a further submenu); this spec does not impose an explicit maximum depth beyond what remains usable within the viewport.

### Requirement 4 — Disabled items

**User Story:** As a user, I want an action that isn't currently valid to still be visible but clearly unavailable, so that I understand the action exists without being able to trigger it by mistake.

#### Acceptance Criteria

1. WHEN a menu item definition is marked disabled THEN the system SHALL render it with a visually muted treatment distinct from an enabled item, per the same "clearly intentional, not broken" theming standard `diagram-ide-mockup`'s placeholders already established, rather than simply omitting it.
2. WHEN the user clicks, or keyboard-activates, a disabled item THEN the system SHALL NOT invoke its action callback and SHALL NOT close the menu as a result of that interaction.
3. A disabled item MAY still open its submenu, if it has one, so a user can discover further-nested actions even when the parent item itself isn't directly actionable, unless the consumer disables the item's children individually.
4. WHEN a menu item definition provides an explanatory reason alongside its disabled state THEN the system SHOULD surface it (e.g. as a tooltip) rather than leaving the user to guess why the item is unavailable.

### Requirement 5 — Composable menu content

**User Story:** As a developer adding a new feature elsewhere in the app, I want to contribute my own context-menu items without editing a shared, ever-growing menu-definition file, so that unrelated features stay decoupled.

#### Acceptance Criteria

1. WHEN `ContextMenu` is given its items THEN the system SHALL accept them as one or more independently-produced groups (ordered lists of items) rather than requiring a single, pre-merged flat list.
2. WHEN more than one group is supplied THEN the system SHALL render a visual separator between groups, so the menu communicates which items came from the same logical source without any group needing to know about any other.
3. WHEN two or more call sites each produce their own group(s) for the same menu (e.g. a core group plus a diagram-type-specific group) THEN combining those groups into the list `ContextMenu` renders SHALL require nothing more than concatenating plain data — no shared registry, subscription, or plugin-loading mechanism is required for this spec's scope.
4. A submenu's items (Requirement 3) SHALL support the same grouped structure as the top-level menu, so composability applies at every nesting level, not only the root.
5. This spec SHALL NOT provide an automatic, app-wide "contribution point" mechanism (e.g. a global registry that diagram-type modules push into without the render call site assembling the groups itself) — assembling the final group list remains the responsibility of whoever renders `ContextMenu` for a given surface; that is a reasonable extension a future spec MAY add on top of this one.

### Requirement 6 — Keyboard accessibility

**User Story:** As a keyboard user, I want to operate an open context menu without a mouse, so that the menu is usable the way the rest of a familiar IDE surface already is.

#### Acceptance Criteria

1. WHEN a `ContextMenu` opens THEN the system SHALL move keyboard focus into it, so arrow keys immediately navigate its items.
2. WHEN the user presses `ArrowUp`/`ArrowDown` THEN the system SHALL move focus to the previous/next enabled item, skipping disabled items and group-separator boundaries transparently.
3. WHEN the user presses `ArrowRight` on a focused item with a submenu THEN the system SHALL open and move focus into that submenu; WHEN the user presses `ArrowLeft` within a submenu THEN the system SHALL close it and return focus to its parent item.
4. WHEN the user presses `Enter` or `Space` on a focused, enabled item THEN the system SHALL activate it exactly as a click would (Requirement 1.4).
5. WHEN the menu closes (Requirement 1.3) THEN the system SHALL return keyboard focus to whatever element had it before the menu opened, so closing the menu doesn't strand focus.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: `ContextMenu` renders and interacts with a menu structure it's handed; it SHALL NOT contain logic specific to any consumer (file operations, diagram operations, ribbon commands).
- **Modular Design**: submenu rendering (Requirement 3) and keyboard navigation (Requirement 6) SHALL be implemented so they apply uniformly at every nesting level via the same code path, not duplicated per level.
- **Reuse over reinvention**: item icons SHALL use the client's existing `@mdi/font` dependency and all styling SHALL use `index.css`'s existing themed CSS custom properties — no new icon package, no component-local color palette.

### Performance

- Opening a `ContextMenu` (including viewport-repositioning, Requirement 1.2) SHALL be visually instantaneous — no perceptible delay between the triggering interaction and the menu appearing.

### Security

- Not applicable beyond ordinary rendering: `ContextMenu` has no backend/network dependency of its own: it renders data and invokes callbacks it's given, both entirely a consumer's responsibility.

### Reliability

- IF a consumer supplies an empty items list (no groups, or all groups empty) THEN the system SHALL render nothing rather than an empty floating menu shell, and SHOULD warn during development (e.g. a console warning) since this usually indicates a consumer bug.

### Usability

- Visual styling (spacing, hover/active states, disabled treatment, submenu indicator) SHOULD follow the same familiar-IDE-conventions standard already guiding `diagram-ide-mockup`'s ribbon and tabs, so a context menu feels like it belongs to the same product rather than a bolted-on library's default look.
