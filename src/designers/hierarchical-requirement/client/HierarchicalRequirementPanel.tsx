import { useCallback } from "react";
import { elementSelectionOf } from "@client/canvas/selection";
import { useHierarchySurfaceStream } from "@client/designers/useHierarchySurfaceStream";
import { HierarchySurface } from "@client/hierarchySurface/library/HierarchySurface";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { hierarchicalRequirementSurface } from "./hierarchicalRequirementDefinition";

/**
 * A requirement document, shown as one page of nested boxes. The module's whole client is this: the page's stream
 * handed to the hierarchy surface library with the designer's declarations. It draws nothing itself, and that it is
 * opening, cannot be opened or that a change was refused are said by the frame the shell puts around every tool,
 * which the stream reports to.
 */
export function HierarchicalRequirementPanel({ path, entryId }: ToolContentProps) {
  const surface = useHierarchySurfaceStream(path);
  const { gesture } = surface;

  // One selected entry is the selection of the whole application, as a table's row is.
  const { select } = useContextConnection();
  const onSelectNode = useCallback((id: string) => select(id === "" ? null : elementSelectionOf(entryId, path, id)), [select, entryId, path]);
  const onGesture = useCallback(
    (operation: string, targetId: string, params: Readonly<Record<string, string>>) => gesture(operation, targetId, params),
    [gesture],
  );

  return (
    <HierarchySurface
      model={surface.model}
      definition={hierarchicalRequirementSurface}
      pendingEdits={surface.pendingEdits}
      onGesture={onGesture}
      onSelectNode={onSelectNode}
    />
  );
}
