import { AppHeader } from "../components/AppHeader";
import { ContextConnectionProvider } from "./context/ContextConnectionProvider";
import { ShellPromptHost } from "./context/ShellPromptHost";
import { NoticeHost } from "./context/NoticeHost";
import { RibbonBar } from "./ribbon/RibbonBar";
import { SplitPane } from "./panes/SplitPane";
import { TabbedPane } from "./panes/TabbedPane";
import { DiagramTabsPanel } from "./panels/DiagramTabsPanel";
import { DiagramViewProvider } from "./panels/DiagramViewContext";
import { DiagramToolboxProvider } from "./panels/DiagramToolboxContext";
import { InlineLabelPlacementProvider } from "./panels/InlineLabelPlacementContext";
import { ErrorsWarningsPanel } from "./panels/ErrorsWarningsPanel";
import { HierarchyPanel } from "./panels/HierarchyPanel";
import { PropertyGridPanel } from "./panels/PropertyGridPanel";
import { SearchPanel } from "./panels/SearchPanel";
import { ToolboxPanel } from "./panels/ToolboxPanel";

export interface WorkspaceShellProps {
  projectId: Uint8Array;
  projectName: string;
  onBack: () => void;
}

export function WorkspaceShell({ projectId, projectName, onBack }: WorkspaceShellProps) {
  return (
    // One context connection per opened project: every panel below reads the shared
    // selection through hooks rather than wiring callbacks to each other.
    <ContextConnectionProvider projectId={projectId}>
      <DiagramViewProvider>
      <DiagramToolboxProvider>
      <InlineLabelPlacementProvider>
      <div className="shell">
        <div className="shell-header">
          <button type="button" className="shell-header-back" onClick={onBack}>
            &larr; Back to projects
          </button>
          <span className="shell-project-name">{projectName}</span>
          <AppHeader />
        </div>
        <RibbonBar />
        <div className="shell-body">
          <SplitPane
            direction="horizontal"
            initialSplit={0.2}
            minSize={160}
            first={
              <TabbedPane
                tabs={[
                  { id: "hierarchy", label: "Hierarchy", icon: "mdi-file-tree", content: <HierarchyPanel projectId={projectId} /> },
                  { id: "search", label: "Search", icon: "mdi-magnify", content: <SearchPanel /> },
                ]}
              />
            }
            second={
              <SplitPane
                direction="horizontal"
                initialSplit={0.78}
                minSize={160}
                first={
                  <SplitPane
                    direction="vertical"
                    initialSplit={0.7}
                    minSize={120}
                    first={<DiagramTabsPanel projectId={projectId} />}
                    second={
                      <TabbedPane
                        tabs={[
                          {
                            id: "problems",
                            label: "Errors & Warnings",
                            icon: "mdi-alert-circle-outline",
                            content: <ErrorsWarningsPanel />,
                          },
                        ]}
                      />
                    }
                  />
                }
                second={
                  <TabbedPane
                    tabs={[
                      { id: "toolbox", label: "Toolbox", icon: "mdi-toolbox-outline", content: <ToolboxPanel /> },
                      {
                        id: "properties",
                        label: "Properties",
                        icon: "mdi-tune-variant",
                        content: <PropertyGridPanel />,
                      },
                    ]}
                  />
                }
              />
            }
          />
        </div>
        <ShellPromptHost />
        <NoticeHost />
      </div>
      </InlineLabelPlacementProvider>
      </DiagramToolboxProvider>
      </DiagramViewProvider>
    </ContextConnectionProvider>
  );
}
