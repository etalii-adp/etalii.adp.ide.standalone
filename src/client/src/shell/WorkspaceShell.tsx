import { RibbonBar } from "./RibbonBar";
import { SplitPane } from "./SplitPane";
import { TabbedPane } from "./TabbedPane";
import { DiagramPanel } from "./panels/DiagramPanel";
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
    <div className="shell">
      <div className="shell-header">
        <button type="button" className="shell-header-back" onClick={onBack}>
          &larr; Back to projects
        </button>
        <span className="shell-project-name">{projectName}</span>
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
                  first={
                    <TabbedPane
                      tabs={[
                        {
                          id: "diagram-1",
                          label: "Diagram 1",
                          icon: "mdi-file-tree-outline",
                          content: <DiagramPanel />,
                        },
                        {
                          id: "diagram-2",
                          label: "Diagram 2",
                          icon: "mdi-file-tree-outline",
                          content: <DiagramPanel />,
                        },
                      ]}
                    />
                  }
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
                      label: "Property Grid",
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
    </div>
  );
}
