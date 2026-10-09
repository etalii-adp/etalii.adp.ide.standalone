import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  AadElementPayloadSchema,
  AadRelationPayloadSchema,
  AadViewPayloadSchema,
  type AadElementPayload,
  type AadRelationPayload,
} from "@client/generated/agent-activity-diagram_pb";
import { AAD_TYPE_PREFIX, AadElementTypes, AadWireRelationType } from "./aadIds";

/** One drawn element: a project, a specification, an agent, a location or an environment. */
export interface AadElement {
  id: string;
  /** Which of the five it is: the wire type without its prefix. */
  type: string;
  x: number;
  y: number;
  payload: AadElementPayload;
}

export interface AadRelation {
  id: string;
  payload: AadRelationPayload;
}

/** The stream folded into what the canvas draws. */
export interface AadModel {
  elements: ReadonlyMap<string, AadElement>;
  relations: ReadonlyMap<string, AadRelation>;
  /** Whether archived specifications are shown: the document's value, not the canvas's. */
  showArchived: boolean;
}

export const emptyModel: AadModel = { elements: new Map(), relations: new Map(), showArchived: false };

const DRAWN: ReadonlySet<string> = new Set(
  [AadElementTypes.project, AadElementTypes.specification, AadElementTypes.agent, AadElementTypes.location, AadElementTypes.environment].map(
    (type) => `${AAD_TYPE_PREFIX}${type}`,
  ),
);
const RELATION_TYPE = `${AAD_TYPE_PREFIX}${AadWireRelationType}`;
const VIEW_TYPE = `${AAD_TYPE_PREFIX}${AadElementTypes.view}`;

/** Folds one delta into the model. A delta of a kind this diagram does not use leaves it as it is. */
export function applyDelta(model: AadModel, delta: Delta): AadModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: AadModel, incoming: readonly Element[]): AadModel {
  const elements = new Map(model.elements);
  const relations = new Map(model.relations);
  let showArchived = model.showArchived;
  for (const element of incoming) {
    const id = element.id?.value;
    if (!id || !element.payload) {
      continue;
    }
    const bytes = element.payload.value;
    if (DRAWN.has(element.type)) {
      elements.set(id, {
        id,
        type: element.type.slice(AAD_TYPE_PREFIX.length),
        x: element.position?.x ?? 0,
        y: element.position?.y ?? 0,
        payload: fromBinary(AadElementPayloadSchema, bytes),
      });
    } else if (element.type === RELATION_TYPE) {
      relations.set(id, { id, payload: fromBinary(AadRelationPayloadSchema, bytes) });
    } else if (element.type === VIEW_TYPE) {
      showArchived = fromBinary(AadViewPayloadSchema, bytes).showArchived;
    }
  }
  return { elements, relations, showArchived };
}

function removed(model: AadModel, ids: readonly { value: string }[]): AadModel {
  const elements = new Map(model.elements);
  const relations = new Map(model.relations);
  for (const id of ids) {
    elements.delete(id.value);
    relations.delete(id.value);
  }
  return { elements, relations, showArchived: model.showArchived };
}
