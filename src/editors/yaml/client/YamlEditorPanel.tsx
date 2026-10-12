import { useMemo, useRef, useState, useCallback } from "react";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";
import { SchemaFiles, schemaFileKey, type SchemaFile } from "@client/editors/SchemaFiles";
import { createYamlAnalysis } from "./yamlAnalysis";
import { yamlGrammar } from "./yamlGrammar";
import { yamlKeys } from "./yamlKeys";
import { yamlScalarColour } from "./yamlScalarColour";

/**
 * The YAML editor: the shared text panel with YAML's grammar, its colour and its keys, and the
 * analysis on Red Hat's YAML language service (yaml-editor design, `YamlEditorPanel`). The schema
 * files the analysis names are read by `SchemaFiles`, over the tab's own stream; a file arriving
 * or changing analyses the unchanged text again (Requirement 5.1 and 5.4).
 *
 * The service itself is not imported here: the analysis loads it with `import()` on first use,
 * so the shell's bundle, which imports every module's panel, does not carry it.
 */
export function YamlEditorPanel(props: ToolContentProps) {
  const [paths, setPaths] = useState<readonly (readonly string[])[]>([]);
  const [revision, setRevision] = useState(0);
  const [, setStatusRevision] = useState(0);
  const files = useRef<ReadonlyMap<string, SchemaFile>>(new Map());
  const documentPath = useRef(props.path);
  documentPath.current = props.path;

  const analysis = useMemo(
    () =>
      createYamlAnalysis({
        get documentPath() {
          return documentPath.current;
        },
        schemaFiles: () => files.current,
        needSchemas: (needed) => {
          // Only a different set is a change: the same paths asked for again open nothing new.
          setPaths((current) => (sameKeys(current, needed) ? current : needed));
        },
        statusChanged: () => setStatusRevision((value) => value + 1),
      }),
    [],
  );

  const onFiles = useCallback((next: ReadonlyMap<string, SchemaFile>) => {
    files.current = next;
    setRevision((value) => value + 1);
  }, []);

  return (
    <>
      <SchemaFiles projectId={props.projectId} paths={paths} onChange={onFiles} />
      <TextEditorPanel {...props} extensions={[yamlGrammar, yamlKeys, yamlScalarColour]} analysis={analysis} analysisRevision={revision} />
    </>
  );
}

function sameKeys(a: readonly (readonly string[])[], b: readonly (readonly string[])[]): boolean {
  const keys = new Set(a.map(schemaFileKey));
  return a.length === b.length && b.every((path) => keys.has(schemaFileKey(path)));
}
