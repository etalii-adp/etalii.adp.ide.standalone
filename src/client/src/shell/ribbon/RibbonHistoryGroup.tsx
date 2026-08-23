import type { ContextAction } from "../../generated/context_pb";
import { PROJECT_SOURCE, useContextConnection, useProjectActions } from "../context/ContextConnectionProvider";
import { tooltipFor } from "./RibbonContextualGroups";

/**
 * The ribbon's Undo/Redo, rendered straight from the project actions the backend pushes
 * (diagram-undo-redo Requirement 6). Unlike the contextual groups there is no last-shown
 * fallback to keep: project actions do not come and go with a selection, so a disabled button
 * is disabled because the backend said so, never because a selection is momentarily absent.
 * A pure subscriber - it holds no state and makes no call but ExecuteAction on the project.
 */
export function RibbonHistoryGroup() {
  const { executeAction } = useContextConnection();
  const projectActions = useProjectActions();

  if (projectActions.length === 0) {
    // Nothing pushed yet (a connection still opening): the group is simply absent until the
    // baseline arrives, rather than a row of buttons that cannot yet do anything.
    return null;
  }

  const run = (action: ContextAction) => {
    void executeAction(action.id, PROJECT_SOURCE);
  };

  return (
    <>
      {projectActions.map((group, groupIndex) => (
        <div className="ribbon-group ribbon-group-history" key={groupIndex}>
          {group.actions.map((action) => (
            <button
              key={action.id}
              type="button"
              className="ribbon-button"
              title={tooltipFor(action)}
              disabled={!action.available}
              onClick={() => run(action)}
            >
              <span className={`mdi ${action.icon}`} aria-hidden="true" />
              <span className="ribbon-button-label">{action.label}</span>
            </button>
          ))}
        </div>
      ))}
    </>
  );
}
