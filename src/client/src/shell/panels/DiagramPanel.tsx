import { PanelPlaceholder } from "./PanelPlaceholder";
import { canvasFor } from "./diagramCanvases";
import { ResolvedTextEditorPanel } from "@client/editors/ResolvedTextEditorPanel";
import { CanvasFrame } from "@client/canvas/library/surface/CanvasFrame";

/** Which diagram a panel shows: its project, the `.adp` entry's id, its project-relative path, and its type. */
export interface OpenDiagram {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: string[];
  /** The MIME type from the `.adp` file's first line; decides which canvas renders it. */
  mimeType: string;
  /** Forces the editor family on the tab's stream - set only for "Open as text" tabs (R5.2). */
  editorId?: string;
  /** 1-based line to scroll to once loaded - go-to-line from a problem (R8.1). */
  initialLine?: number;
}

export interface DiagramPanelProps {
  /** The diagram to show, or undefined for the empty tab the mockup still opens. */
  diagram?: OpenDiagram;
}

/**
 * Hosts a diagram's canvas, whichever module claims its type.
 *
 * This panel names no diagram type. It asks the registry which module claims the MIME type and
 * renders what that module supplies - a canvas, or its own explanation of why there is none
 * (diagram-workspace-tabs Requirement 4.4). A type no module claims falls through to the
 * generic placeholder, which is the only case the shell still speaks for.
 *
 * The tab system (`DiagramTabsPanel`) always hands this panel a diagram; the no-diagram
 * fallback stays only as a safety net.
 */
export function DiagramPanel({ diagram }: DiagramPanelProps) {
  if (diagram === undefined || diagram.mimeType === "") {
    return (
      <PanelPlaceholder
        title="Diagram"
        description="The diagram canvas for viewing and editing."
        futureSpec="adp-diagram-ide"
      />
    );
  }

  // An "Open as text" tab whose editor the backend has yet to name: the stream itself will,
  // and this panel mounts the right module's canvas when it does. Handled here, before the
  // registry, because it is the shell's own gesture rather than any module's claim.
  if (diagram.mimeType === "editor/*") {
    return (
      <CanvasFrame>
        <ResolvedTextEditorPanel
          projectId={diagram.projectId}
          entryId={diagram.entryId}
          path={diagram.path}
          editorId={diagram.editorId}
          initialLine={diagram.initialLine}
        />
      </CanvasFrame>
    );
  }

  const registration = canvasFor(diagram.mimeType);

  // Every module canvas sits inside the library's frame, which is the one place a refusal is shown
  // and the one appearance of opening, reconnecting and unavailable (client-centralization
  // Requirement 2). The shell places it, so no module declares or wires one.
  if (registration?.Canvas !== undefined) {
    const Canvas = registration.Canvas;
    return (
      <CanvasFrame>
        <Canvas
          projectId={diagram.projectId}
          entryId={diagram.entryId}
          path={diagram.path}
          editorId={diagram.editorId}
          initialLine={diagram.initialLine}
        />
      </CanvasFrame>
    );
  }

  return (
    <PanelPlaceholder
      title={diagram.path[diagram.path.length - 1] ?? "Diagram"}
      description={
        registration?.unsupported?.description ??
        `No canvas can render ${diagram.mimeType} diagrams yet.`
      }
      futureSpec={registration?.unsupported?.futureSpec ?? "adp-diagram-ide"}
    />
  );
}
