import { ExplorerTreePanel } from "./ExplorerTreePanel";

export interface HierarchyPanelProps {
  projectId: Uint8Array;
}

export function HierarchyPanel({ projectId }: HierarchyPanelProps) {
  return <ExplorerTreePanel projectId={projectId} />;
}
