import { PanelPlaceholder } from "./PanelPlaceholder";
import { panelFor } from "./toolPanels";
import { ResolvedTextEditorPanel } from "@client/editors/ResolvedTextEditorPanel";
import { CanvasFrame } from "@client/canvas/library/surface/CanvasFrame";

/** Which document a tool panel shows: its project, the `.adp` entry's id, its project-relative path, and its type. */
export interface OpenTool {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: string[];
  /** The MIME type from the `.adp` file's first line; decides which module's panel renders it. */
  mimeType: string;
  /** Forces the editor family on the tab's stream - set only for "Open as text" tabs (R5.2). */
  editorId?: string;
  /** 1-based line to scroll to once loaded - go-to-line from a problem (R8.1). */
  initialLine?: number;
}

export interface ToolPanelProps {
  /** The document to show, or undefined for the empty tab the mockup still opens. */
  tool?: OpenTool;
}

/**
 * Hosts a tool - a diagram's canvas or an editor's text panel - whichever module claims its type.
 *
 * This panel names no tool type. It asks the registry which module claims the MIME type and
 * renders what that module supplies - a panel, or its own explanation of why there is none
 * (diagram-workspace-tabs Requirement 4.4). A type no module claims falls through to the
 * generic placeholder, which is the only case the shell still speaks for.
 *
 * The tab system (`ToolTabsPanel`) always hands this panel a document; the no-document
 * fallback stays only as a safety net.
 */
export function ToolPanel({ tool }: ToolPanelProps) {
  if (tool === undefined || tool.mimeType === "") {
    return (
      <PanelPlaceholder
        title="Tool"
        description="The tool panel, where a diagram, designer or editor opens for viewing and editing."
        futureSpec="adp-diagram-ide"
      />
    );
  }

  // An "Open as text" tab whose editor the backend has yet to name: the stream itself will,
  // and this panel mounts the right module's canvas when it does. Handled here, before the
  // registry, because it is the shell's own gesture rather than any module's claim.
  if (tool.mimeType === "editor/*") {
    return (
      <CanvasFrame>
        <ResolvedTextEditorPanel
          projectId={tool.projectId}
          entryId={tool.entryId}
          path={tool.path}
          editorId={tool.editorId}
          initialLine={tool.initialLine}
        />
      </CanvasFrame>
    );
  }

  const registration = panelFor(tool.mimeType);

  // Every module canvas sits inside the library's frame, which is the one place a refusal is shown
  // and the one appearance of opening, reconnecting and unavailable (client-centralization
  // Requirement 2). The shell places it, so no module declares or wires one.
  if (registration?.Panel !== undefined) {
    const Panel = registration.Panel;
    return (
      <CanvasFrame>
        <Panel
          projectId={tool.projectId}
          entryId={tool.entryId}
          path={tool.path}
          editorId={tool.editorId}
          initialLine={tool.initialLine}
        />
      </CanvasFrame>
    );
  }

  return (
    <PanelPlaceholder
      title={tool.path[tool.path.length - 1] ?? "Tool"}
      description={
        registration?.unsupported?.description ??
        `No module can show ${tool.mimeType} yet.`
      }
      futureSpec={registration?.unsupported?.futureSpec ?? "adp-diagram-ide"}
    />
  );
}
