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
 * Hosts a diagram's canvas. A mindmap renders through {@link MindmapCanvas}; an openable type
 * this build has no canvas for says so by name rather than showing a blank surface
 * (diagram-workspace-tabs Requirement 4.4). The tab system (`DiagramTabsPanel`) always hands
 * this panel a diagram; the no-diagram fallback stays only as a safety net.
 */
export function DiagramPanel({ diagram }: DiagramPanelProps) {
  if (diagram?.mimeType === "freeplane/mindmap") {
    return <MindmapCanvas projectId={diagram.projectId} entryId={diagram.entryId} path={diagram.path} />;
  }

  if (diagram !== undefined && diagram.mimeType !== "") {
    return (
      <PanelPlaceholder
        title={diagram.path[diagram.path.length - 1] ?? "Diagram"}
        description={`No canvas can render ${diagram.mimeType} diagrams yet.`}
        futureSpec="adp-diagram-ide"
      />
    );
  }

  return (
    <PanelPlaceholder
      title="Diagram"
      description="The diagram canvas for viewing and editing."
      futureSpec="adp-diagram-ide"
    />
  );
}
