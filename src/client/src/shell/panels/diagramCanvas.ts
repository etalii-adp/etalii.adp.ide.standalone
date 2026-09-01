import type { ComponentType } from "react";

/**
 * What every diagram canvas is handed: which diagram to draw. The `.adp` entry identifies the
 * registration; the path is project-relative and is what the canvas shows as a title.
 */
export interface DiagramCanvasProps {
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
 * One diagram type's - or one family's - claim on the canvas.
 *
 * A module that can draw its types supplies a {@link Canvas}. A module that knows about a type
 * this build cannot draw supplies {@link unsupported} instead, so the explanation comes from
 * the module that understands the notation rather than from a fallback in the shell.
 */
export interface DiagramCanvasRegistration {
  /** Which MIME types this entry speaks for. A family may claim several. */
  matches: (mimeType: string) => boolean;
  /** The canvas to render, when this build can draw the type. */
  Canvas?: ComponentType<DiagramCanvasProps>;
  /** Why there is no canvas, when there is not. */
  unsupported?: {
    description: string;
    /** The spec that would add it, shown by `PanelPlaceholder` so the gap is traceable. */
    futureSpec: string;
  };
}

/**
 * What a diagram module's `client/register.ts` exports.
 *
 * The shell finds these by scanning every `src/diagrams/<type>/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies. Adding a diagram type means adding a module, not editing the
 * shell.
 */
export interface DiagramClientModule {
  registrations: DiagramCanvasRegistration[];
}
