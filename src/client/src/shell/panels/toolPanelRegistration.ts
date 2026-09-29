import type { ComponentType } from "react";

/**
 * What every registered panel is handed - a diagram's canvas, an editor's text panel, and later a
 * designer's canvas: which document to show. The `.adp` entry identifies the registration; the
 * path is project-relative and is what the panel shows as a title.
 */
export interface ToolContentProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: string[];
  /**
   * Forces the editor family on the tab's stream: "*" for whichever editor the backend's
   * resolver answers, a definition id for one the user chose through "Open with…". Unset for
   * every ordinary tab - diagram canvases ignore it (modular-text-editors R5.2, R4.4).
   */
  editorId?: string;
  /** 1-based line to scroll to once the text is loaded - go-to-line from a problem (R8.1). */
  initialLine?: number;
}

/**
 * One tool type's - or one family's - claim on the tool panel. Diagrams, designers and editors
 * register alike (naming convention: shared by all kinds, so "tool").
 *
 * A module that can show its types supplies a {@link Panel}: a diagram's canvas, an editor's text
 * panel. A module that knows about a type this build cannot show supplies {@link unsupported}
 * instead, so the explanation comes from the module that understands the notation rather than
 * from a fallback in the shell.
 */
export interface ToolPanelRegistration {
  /** Which MIME types this entry speaks for. A family may claim several. */
  matches: (mimeType: string) => boolean;
  /** The panel to render, when this build can show the type: a canvas for a diagram, a text panel for an editor. */
  Panel?: ComponentType<ToolContentProps>;
  /** Why there is no panel, when there is not. */
  unsupported?: {
    description: string;
    /** The spec that would add it, shown by `PanelPlaceholder` so the gap is traceable. */
    futureSpec: string;
  };
}

/**
 * What a diagram, designer or editor module's `client/register.ts` exports.
 *
 * The shell finds these by scanning every module's `client/register.ts` under `src/diagrams`,
 * `src/designers` and `src/editors` - it holds
 * no list of tool types, exactly as the backend holds none and discovers `Diagram.Definitions`
 * and `Editor.Definitions` by scanning its own assemblies. Adding a tool type means adding a
 * module, not editing the shell.
 */
export interface ToolClientModule {
  registrations: ToolPanelRegistration[];
}
