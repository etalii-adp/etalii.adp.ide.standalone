import { PanelPlaceholder } from "./PanelPlaceholder";
import { TOOLBOX_DRAG_TYPE, useDiagramToolbox } from "./DiagramToolboxContext";
import type { ToolboxItem } from "../../generated/diagrams_pb";

/**
 * The palette of elements the open diagram's type contributes, described by the backend as
 * data (`DiagramService.DescribeToolbox`) and rendered here without interpretation. Each
 * entry is draggable; the drag carries the backend-named drop action id, and the canvas
 * that accepts the drop executes that action - so the panel adds no behaviour of its own.
 */
export function ToolboxPanel() {
  const items = useDiagramToolbox();

  if (items === null) {
    return (
      <PanelPlaceholder
        title="Toolbox"
        description="Open a diagram to see the elements its type offers."
        futureSpec="adp-diagram-ide"
      />
    );
  }

  if (items.length === 0) {
    return (
      <div className="toolbox-panel" data-testid="toolbox-panel">
        <p className="toolbox-panel-empty">This diagram type offers no toolbox elements.</p>
      </div>
    );
  }

  return (
    <div className="toolbox-panel" data-testid="toolbox-panel">
      <ul className="toolbox-panel-list">
        {items.map((item) => (
          <ToolboxEntry key={item.id} item={item} />
        ))}
      </ul>
    </div>
  );
}

function ToolboxEntry({ item }: { item: ToolboxItem }) {
  const onDragStart = (event: React.DragEvent) => {
    // The payload is the backend's own action id - the one thing the drop needs. The panel
    // neither knows nor cares what the action does.
    event.dataTransfer.setData(TOOLBOX_DRAG_TYPE, item.dropActionId);
    event.dataTransfer.effectAllowed = "copy";
  };

  return (
    <li className="toolbox-panel-item" title={item.description} draggable onDragStart={onDragStart}>
      <span className={`mdi ${item.icon}`} aria-hidden="true" />
      <span className="toolbox-panel-item-label">{item.label}</span>
    </li>
  );
}
