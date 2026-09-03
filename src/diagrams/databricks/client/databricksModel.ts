import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  DatabricksEdgePayloadSchema,
  DatabricksPipelineNodePayloadSchema,
  DatabricksResourcePayloadSchema,
  DatabricksTargetPayloadSchema,
  DatabricksTaskPayloadSchema,
} from "@client/generated/databricks_pb";

/** The mime-style element types the backend sends, per diagram type of the family. */
export const TASK = "databricks/job+task";
export const CLUSTER = "databricks/job+cluster";
export const EDGE = "databricks/job+edge";
export const BUNDLE = "databricks/bundle+bundle";
export const RESOURCE = "databricks/bundle+resource";
export const TARGET = "databricks/bundle+target";
export const OVERRIDE_EDGE = "databricks/bundle+edge";
export const PIPELINE_NODE = "databricks/pipeline+node";
export const FLOW_EDGE = "databricks/pipeline+edge";

/**
 * One box on any of the family's canvases, positioned in the module's own coordinate space -
 * computed layout with the `.adp`'s authored positions already overlaid backend-side. The
 * canvas layers its zoom on top; nothing here is a pixel.
 */
export interface DatabricksNode {
  id: string;
  x: number;
  y: number;
  /** What the box is: task, cluster, bundle, resource, source, pipeline, target-node and kin. */
  kind: string;
  label: string;
  /** Small facts worn on the box: a run_if, a cluster binding, serverless, a channel. */
  badges: string[];
  /** A depends_on stub no task declares - drawn marked-missing (Requirement 4.5). */
  unresolved: boolean;
}

/** One target frame of the bundle diagram, drawn behind the nodes. */
export interface DatabricksFrame {
  id: string;
  x: number;
  y: number;
  label: string;
  mode: string;
  isDefault: boolean;
  overrideCount: number;
}

/** One edge - a dependency, an override, a flow line - between two elements, by id. */
export interface DatabricksEdge {
  id: string;
  fromElementId: string;
  toElementId: string;
  /** A condition task's outcome this edge follows: "", "true" or "false" (Requirement 4.3). */
  outcome: string;
  kind: "depends" | "override" | "flow";
}

export interface DatabricksModel {
  nodes: Map<string, DatabricksNode>;
  frames: Map<string, DatabricksFrame>;
  edges: Map<string, DatabricksEdge>;
}

export const emptyModel: DatabricksModel = {
  nodes: new Map(),
  frames: new Map(),
  edges: new Map(),
};

/**
 * Folds one delta into the model. `add` is an upsert keyed on id: an edit arrives as an add
 * carrying the element in its new state, never as a remove-then-add.
 */
export function applyDelta(model: DatabricksModel, delta: Delta): DatabricksModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: DatabricksModel, elements: readonly Element[]): DatabricksModel {
  const next: DatabricksModel = {
    nodes: new Map(model.nodes),
    frames: new Map(model.frames),
    edges: new Map(model.edges),
  };

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode. Several modules share one delta stream, and a
    // foreign element decoded as ours throws and takes the whole delta with it - a sibling
    // module shipped exactly that bug before finding it.
    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    switch (element.type) {
      case TASK: {
        const payload = fromBinary(DatabricksTaskPayloadSchema, bytes);
        const badges: string[] = [];
        if (payload.runIf) {
          badges.push(payload.runIf);
        }
        badges.push(payload.clusterKey ? payload.clusterKey : "serverless");
        next.nodes.set(id, {
          id,
          x,
          y,
          kind: payload.taskType || "task",
          label: payload.taskKey,
          badges,
          unresolved: payload.unresolved,
        });
        break;
      }
      case BUNDLE:
      case RESOURCE: {
        const payload = fromBinary(DatabricksResourcePayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          kind: payload.kind || "resource",
          label: payload.key || payload.kind,
          badges: element.type === BUNDLE ? [] : [payload.kind],
          unresolved: false,
        });
        break;
      }
      case CLUSTER:
      case PIPELINE_NODE: {
        const payload = fromBinary(DatabricksPipelineNodePayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          kind: payload.role || "node",
          label: payload.label,
          badges: [...payload.badges],
          unresolved: false,
        });
        break;
      }
      case TARGET: {
        const payload = fromBinary(DatabricksTargetPayloadSchema, bytes);
        next.frames.set(id, {
          id,
          x,
          y,
          label: id.startsWith("target:") ? id.slice("target:".length) : id,
          mode: payload.mode,
          isDefault: payload.isDefault,
          overrideCount: payload.overrideCount,
        });
        break;
      }
      case EDGE:
      case OVERRIDE_EDGE:
      case FLOW_EDGE: {
        const payload = fromBinary(DatabricksEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          outcome: payload.outcome,
          kind: element.type === EDGE ? "depends" : element.type === OVERRIDE_EDGE ? "override" : "flow",
        });
        break;
      }
      default:
        // Not ours; skipped, never decoded.
        break;
    }
  }

  return next;
}

function removed(model: DatabricksModel, ids: readonly { value: string }[]): DatabricksModel {
  const next: DatabricksModel = {
    nodes: new Map(model.nodes),
    frames: new Map(model.frames),
    edges: new Map(model.edges),
  };

  for (const id of ids) {
    next.nodes.delete(id.value);
    next.frames.delete(id.value);
    next.edges.delete(id.value);
  }

  return next;
}
