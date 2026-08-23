import { PanelPlaceholder } from "./PanelPlaceholder";
import { MindmapCanvas } from "./mindmap/MindmapCanvas";

/** Which diagram a panel shows: its project, the `.adp` entry's id, its project-relative path, and its type. */
export interface OpenDiagram {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: string[];
  /** The MIME type from the `.adp` file's first line; decides which canvas renders it. */
  mimeType: string;
}

export interface DiagramPanelProps {
  /** The diagram to show, or undefined for the empty tab the mockup still opens. */
  diagram?: OpenDiagram;
}

/**
 * Hosts a diagram's canvas. A mindmap renders through {@link MindmapCanvas}; a type without a
 * canvas yet falls back to the placeholder, as does an empty tab. Opening a diagram from a
 * selection - choosing which tab shows which file - belongs to the workspace's tab system
 * (`diagram-ide-mockup`), which is still a mockup; this panel is ready for it.
 */
export function DiagramPanel({ diagram }: DiagramPanelProps) {
  if (diagram?.mimeType === "freeplane/mindmap") {
    return <MindmapCanvas projectId={diagram.projectId} entryId={diagram.entryId} path={diagram.path} />;
  }

  return (
    <PanelPlaceholder
      title="Diagram"
      description="The diagram canvas for viewing and editing."
      futureSpec="adp-diagram-ide"
    />
  );
}
