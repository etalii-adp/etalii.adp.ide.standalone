import { useCallback, useEffect, useRef } from "react";
import { CanvasStatusContext } from "@client/canvas/library/surface/canvasStatus";
import { useEditorText } from "./useEditorText";

/**
 * Reads the schema files an editor's analysis needs, over the connection's existing stream, as a
 * text tab reads its own file (json-editor Requirement 9.6, ruling A1). No call and no stream is
 * added: each path is one more document on the tab's stream, confined to the project by the code
 * that confines a tab's path, and a change on disk arrives by itself (Requirement 5.8).
 *
 * Renders nothing. The set of paths may grow as `$ref`s are followed and shrink as the document
 * changes; each path is a child that opens and closes its file. Every arrival, change and failure
 * is reported upward as a new map, keyed by the path joined with `/`.
 */
export type SchemaFile =
  | { state: "loading" }
  | { state: "loaded"; text: string; revision: number }
  | { state: "failed"; reason: string };

export interface SchemaFilesProps {
  projectId: Uint8Array;
  paths: readonly (readonly string[])[];
  onChange: (files: ReadonlyMap<string, SchemaFile>) => void;
}

export function schemaFileKey(path: readonly string[]): string {
  return path.join("/");
}

export function SchemaFiles({ projectId, paths, onChange }: SchemaFilesProps) {
  const files = useRef<ReadonlyMap<string, SchemaFile>>(new Map());
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const report = useCallback((key: string, file: SchemaFile | undefined) => {
    const next = new Map(files.current);
    if (file === undefined) {
      next.delete(key);
    } else {
      next.set(key, file);
    }
    files.current = next;
    onChangeRef.current(next);
  }, []);

  const unique = new Map(paths.map((path) => [schemaFileKey(path), path]));

  // A schema's stream never speaks for the tab: a schema that is missing is one information
  // finding in the document (Requirement 5.5), not "unavailable" over the whole editor.
  return (
    <CanvasStatusContext.Provider value={null}>
      {[...unique].map(([key, path]) => (
        <SchemaFileReader key={key} projectId={projectId} path={path} report={report} />
      ))}
    </CanvasStatusContext.Provider>
  );
}

interface SchemaFileReaderProps {
  projectId: Uint8Array;
  path: readonly string[];
  report: (key: string, file: SchemaFile | undefined) => void;
}

function SchemaFileReader({ projectId, path, report }: SchemaFileReaderProps) {
  const { model, failed } = useEditorText(projectId, path);
  const key = schemaFileKey(path);

  useEffect(() => {
    if (failed) {
      report(key, { state: "failed", reason: "was not found in the project" });
    } else if (model.loaded) {
      report(key, { state: "loaded", text: model.text, revision: model.revision });
    } else {
      report(key, { state: "loading" });
    }
  }, [report, key, failed, model.loaded, model.text, model.revision]);

  useEffect(() => () => report(key, undefined), [report, key]);

  return null;
}
