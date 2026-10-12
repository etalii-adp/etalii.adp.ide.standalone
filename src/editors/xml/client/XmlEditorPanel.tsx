import { useCallback, useMemo, useRef, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { EditorState } from "@codemirror/state";
import { setDiagnostics } from "@codemirror/lint";
import { xml } from "@codemirror/lang-xml";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";
import { SchemaFiles, schemaFileKey, type SchemaFile } from "@client/editors/SchemaFiles";
import { useAuth } from "@client/auth/AuthContext";
import { EditorService } from "@client/generated/editors_pb";
import { createXmlAnalysis, type XmlAnalysisCalls } from "./xmlAnalysis";
import { xmlFolding } from "./xmlFolding";
import { xmlPrefixColour } from "./xmlPrefixColour";

/** The editor's id, which the backend's analyser answers to. */
const editorId = "xml";

/**
 * The backend answers about a text after it was typed, so an edit would otherwise leave the
 * previous answer on screen until the next arrives. Every edit takes the findings away at once;
 * the answer about the new text puts its own back (xml-editor Requirement 4.7).
 */
const clearFindingsOnEdit = EditorState.transactionExtender.of((transaction) =>
  transaction.docChanged ? { effects: setDiagnostics(transaction.startState, []).effects } : null,
);

const extensions = [xml(), xmlPrefixColour, xmlFolding, clearFindingsOnEdit];

/**
 * The XML editor: the shared text panel with CodeMirror's XML grammar, which closes a tag as
 * `>` is typed and offers the open element after `</`; and an analysis that asks the backend
 * about the text as typed. Colour comes from the shared scheme, so this module declares none.
 */
export function XmlEditorPanel(props: ToolContentProps & { calls?: XmlAnalysisCalls; analysisDelay?: number }) {
  const { projectId, path, calls: givenCalls, analysisDelay } = props;
  const { transport } = useAuth();
  const pathKey = path.join("/");
  const calls = useMemo<XmlAnalysisCalls>(() => {
    if (givenCalls) {
      return givenCalls;
    }

    const client = createClient(EditorService, transport);
    const request = (content: string) => ({ projectId: { value: projectId }, path: { segments: pathKey.split("/") }, editorId, content });
    return {
      analyze: (text, signal) => client.analyze(request(text), { signal }),
      complete: (text, offset, explicit, signal) => client.complete({ ...request(text), offset, explicit }, { signal }),
    };
  }, [givenCalls, transport, projectId, pathKey]);

  const [, setStatus] = useState("");
  const [dependsOn, setDependsOn] = useState<readonly (readonly string[])[]>([]);
  const analysis = useMemo(
    () =>
      createXmlAnalysis(calls, {
        // The status is read from the analysis as the panel renders; a new one renders it.
        onStatus: setStatus,
        onDependsOn: (paths) => setDependsOn((current) => (sameKeys(current, paths) ? current : paths)),
      }),
    [calls],
  );

  // A file the answer depended on changed on disk: ask again about the unchanged text
  // (Requirement 5.8). Its first arrival is the state the answer was given in, not a change.
  const revisions = useRef(new Map<string, number>());
  const [analysisRevision, setAnalysisRevision] = useState(0);
  const onSchemaFiles = useCallback((files: ReadonlyMap<string, SchemaFile>) => {
    let changed = false;
    for (const [key, file] of files) {
      if (file.state !== "loaded") {
        continue;
      }

      const seen = revisions.current.get(key);
      if (seen !== undefined && seen !== file.revision) {
        changed = true;
      }
      revisions.current.set(key, file.revision);
    }

    if (changed) {
      setAnalysisRevision((revision) => revision + 1);
    }
  }, []);

  return (
    <>
      <TextEditorPanel {...props} extensions={extensions} analysis={analysis} analysisRevision={analysisRevision} analysisDelay={analysisDelay} />
      <SchemaFiles projectId={projectId} paths={dependsOn} onChange={onSchemaFiles} />
    </>
  );
}

function sameKeys(a: readonly (readonly string[])[], b: readonly (readonly string[])[]): boolean {
  return a.length === b.length && a.every((path, index) => schemaFileKey(path) === schemaFileKey(b[index]));
}
