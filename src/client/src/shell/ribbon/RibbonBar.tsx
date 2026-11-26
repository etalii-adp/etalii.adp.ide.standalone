import { RibbonContextualGroups } from "./RibbonContextualGroups";
import { RibbonHistoryGroup } from "./RibbonHistoryGroup";
import { useProjectShortcuts } from "../context/useProjectShortcuts";
import { useDiagramViewControls } from "../panels/DiagramViewContext";

// The project's own actions come first - New… (the Add dialog on the project root), Undo and Redo,
// as the backend pushes them - then View's zoom and fit for whichever diagram canvas is open, then
// what applies to the selection. The File group's Save and New were mock buttons that did nothing,
// which a new user pressed and waited on (etalii.adp spec 092, host test T035).
export function RibbonBar() {
  // The global Undo/Redo shortcuts (Ctrl+Z, Ctrl+Y), mounted once with the shell.
  useProjectShortcuts();

  return (
    <div className="ribbon">
      <RibbonHistoryGroup />
      <RibbonViewGroup />
      {/* The selection-driven part: whatever the backend says applies to the current selection. */}
      <RibbonContextualGroups />
    </div>
  );
}

/**
 * Zoom and fit for the open diagram. A pure passthrough to the mounted canvas's own view
 * controls; with no diagram open there is nothing to zoom, and the buttons say so by being
 * disabled rather than by disappearing.
 */
function RibbonViewGroup() {
  const controls = useDiagramViewControls();
  const buttons = [
    { icon: "mdi-magnify-plus-outline", label: "Zoom In", run: controls?.zoomIn },
    { icon: "mdi-magnify-minus-outline", label: "Zoom Out", run: controls?.zoomOut },
    { icon: "mdi-fit-to-page-outline", label: "Fit to View", run: controls?.fitToView },
  ];

  return (
    <div className="ribbon-group">
      {buttons.map((button) => (
        <button
          key={button.label}
          type="button"
          className="ribbon-button"
          title={controls === null ? "Open a diagram to use this." : button.label}
          disabled={controls === null}
          onClick={() => button.run?.()}
        >
          <span className={`mdi ${button.icon}`} aria-hidden="true" />
          <span className="ribbon-button-label">{button.label}</span>
        </button>
      ))}
    </div>
  );
}
