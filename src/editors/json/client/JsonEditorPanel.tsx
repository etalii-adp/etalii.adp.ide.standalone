import { useCallback, useMemo, useRef, useState } from "react";
import { json } from "@codemirror/lang-json";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";
import { SchemaFiles, type SchemaFile } from "@client/editors/SchemaFiles";
import { createJsonAnalysis } from "./jsonAnalysis";

/** The grammar is the same for every tab, so it is made once. */
const grammar = [json()];

/**
 * The JSON editor: the shared text panel with JSON's grammar for colour and `jsonAnalysis` for
 * findings, completion and hover (json-editor Requirements 3 to 7). The schema files the analysis
 * asks for are read by `SchemaFiles` over the tab's own stream; each arrival or change on disk
 * bumps the revision, and the panel analyses the text again without an edit (5.8).
 */
export function JsonEditorPanel(props: ToolContentProps) {
  const files = useRef<ReadonlyMap<string, SchemaFile>>(new Map());
  const [needed, setNeeded] = useState<string[][]>([]);
  const [revision, setRevision] = useState(0);
  const [, setStatus] = useState("");

  const path = props.path;
  const analysis = useMemo(
    () =>
      createJsonAnalysis({
        documentPath: path,
        schemaFiles: () => files.current,
        onNeeds: setNeeded,
        onStatus: setStatus,
      }),
    // One analysis per document; a path is a document.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [path.join("/")],
  );

  const onFiles = useCallback((next: ReadonlyMap<string, SchemaFile>) => {
    files.current = next;
    setRevision((current) => current + 1);
  }, []);

  return (
    <>
      <SchemaFiles projectId={props.projectId} paths={needed} onChange={onFiles} />
      <TextEditorPanel {...props} extensions={grammar} analysis={analysis} analysisRevision={revision} />
    </>
  );
}
