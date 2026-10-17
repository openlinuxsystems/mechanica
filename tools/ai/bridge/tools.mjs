// Turns the game's mechanica.describe into tool lists for AI models, runs tool calls, and holds what the
// OpenAI-compatible bridge and the MCP server share. No packages.

import { appendFileSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { GameError } from "./game.mjs";

export const REPO = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
export const DOCS = join(REPO, "docs", "ai");

/** Tool names may only use [A-Za-z0-9_-]: "mechanica.join" becomes "mechanica__join". */
export const toolName = (method) => method.replaceAll(".", "__");
export const methodName = (tool) => tool.replaceAll("__", ".");

const SCOPE_ORDER = { observe: 0, play: 1, admin: 2 };

/**
 * The game's methods as tools: {name, method, group, scope, description, parameters}. Only methods the
 * connection's scopes allow are kept; `groups` (area names such as "survival") narrows them further,
 * and `exclude` drops methods by name. mechanica.auth is never offered (the bridge authenticates).
 */
export function toolsFromDescribe(describe, { scopes = ["observe", "play", "admin"], groups = null, exclude = [] } = {}) {
  const allowed = new Set(scopes);
  const wanted = groups ? new Set(groups) : null;
  return describe.methods
    .filter((m) => m.name !== "mechanica.auth" && allowed.has(m.scope) && !exclude.includes(m.name))
    .filter((m) => !wanted || wanted.has(m.group) || m.group === "mechanica")
    .sort((a, b) => (SCOPE_ORDER[a.scope] ?? 9) - (SCOPE_ORDER[b.scope] ?? 9) || a.name.localeCompare(b.name))
    .map((m) => ({
      name: toolName(m.name),
      method: m.name,
      group: m.group,
      scope: m.scope,
      description: clip(`${m.description} (${m.kind}, ${m.group}, ${m.scope} scope)`, 1000),
      parameters: m.params ?? { type: "object", properties: {} },
    }));
}

/** Tools the bridge runs itself, not the game. */
export const LOCAL_TOOLS = [
  {
    name: "bridge_wait",
    description: "Waits a number of seconds of real time (for walking, machines, crops, raids) and returns the game events that arrived meanwhile.",
    parameters: {
      type: "object",
      properties: { seconds: { type: "number", description: "How long to wait, 0.1 to 30 s.", default: 1 } },
      additionalProperties: false,
    },
  },
  {
    name: "bridge_note",
    description: "Saves a note, recommendation or bug report to the notes file of the person running you. Use the report shapes from the bug-hunting guide.",
    parameters: {
      type: "object",
      properties: {
        kind: { type: "string", enum: ["note", "recommendation", "bug"], description: "What it is." },
        title: { type: "string", description: "One line." },
        text: { type: "string", description: "The body: steps, expected and actual for a bug; why and what to change for a recommendation." },
      },
      required: ["kind", "title", "text"],
      additionalProperties: false,
    },
  },
];

/** OpenAI-style function tools. */
export const openAiTools = (tools) =>
  tools.map((t) => ({ type: "function", function: { name: t.name, description: t.description, parameters: t.parameters } }));

/** MCP tools. */
export const mcpTools = (tools) => tools.map((t) => ({ name: t.name, description: t.description, inputSchema: t.parameters }));

/**
 * For models without native tool calling: the tools written out in the prompt, and how to call them.
 * Each tool is one line with its params, so a hundred tools stay readable.
 */
export function promptToolsText(tools) {
  const lines = tools.map((t) => {
    const props = t.parameters?.properties ?? {};
    const required = new Set(t.parameters?.required ?? []);
    const params = Object.entries(props)
      .map(([name, schema]) => `${name}${required.has(name) ? "" : "?"}: ${typeText(schema)}`)
      .join(", ");
    return `- ${t.name}(${params}): ${t.description}`;
  });
  return [
    "You can call these tools. To call one, write a block exactly like this, with JSON inside:",
    "```tool",
    '{"name": "GetPlayer", "arguments": {}}',
    "```",
    "You may write several blocks in one reply; they run in order. The results come back in the next message.",
    "Write no tool block when you have nothing to do.",
    "",
    "Tools:",
    ...lines,
  ].join("\n");
}

function typeText(schema) {
  if (!schema || typeof schema !== "object") return "any";
  if (schema.anyOf) return schema.anyOf.filter((s) => s.type !== "null").map(typeText).join("|") + "|null";
  if (schema.enum) return schema.enum.map((v) => JSON.stringify(v)).join("|");
  if (schema.type === "array") {
    const n = schema.minItems && schema.minItems === schema.maxItems ? schema.minItems : "";
    return `${typeText(schema.items)}[${n}]`;
  }
  if (schema.type === "object" && schema.properties)
    return `{${Object.entries(schema.properties).map(([k, v]) => `${k}: ${typeText(v)}`).join(", ")}}`;
  return schema.type ?? "any";
}

/**
 * Tool calls written in text: ```tool blocks (or ```json blocks with a "name"), else bare
 * {"name": ..., "arguments": ...} objects. Returns [{name, arguments}].
 */
export function parseToolCalls(text) {
  if (!text) return [];
  const calls = [];
  const fenced = /```(?:tool|json|tool_call)?\s*\n?([\s\S]*?)```/g;
  for (const match of text.matchAll(fenced)) calls.push(...callsIn(match[1]));
  if (calls.length > 0) return calls;
  for (const candidate of jsonObjects(text)) calls.push(...callsIn(candidate));
  return calls;
}

function callsIn(text) {
  let value;
  try {
    value = JSON.parse(text.trim());
  } catch {
    return [];
  }
  const list = Array.isArray(value) ? value : [value];
  return list
    .filter((v) => v && typeof v === "object" && typeof (v.name ?? v.tool) === "string")
    .map((v) => ({ name: v.name ?? v.tool, arguments: v.arguments ?? v.args ?? v.parameters ?? {} }));
}

// Balanced {...} spans in free text, outermost first.
function* jsonObjects(text) {
  let depth = 0;
  let start = -1;
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inString) {
      if (c === "\\") i++;
      else if (c === '"') inString = false;
      continue;
    }
    if (c === '"') inString = true;
    else if (c === "{") {
      if (depth++ === 0) start = i;
    } else if (c === "}" && depth > 0 && --depth === 0) yield text.slice(start, i + 1);
  }
}

/**
 * What to show people watching the AI (SetThought) for a reply's text: the words without code blocks or
 * tool-call JSON, on one line, at most max characters. "" when nothing is left.
 */
export function thoughtText(text, max = 280) {
  if (!text) return "";
  let words = text.replace(/```[\s\S]*?(```|$)/g, " ");
  for (const json of [...jsonObjects(words)]) if (callsIn(json).length > 0) words = words.replace(json, " ");
  words = words.replace(/\s+/g, " ").trim();
  return words.length <= max ? words : `${words.slice(0, max - 1).trimEnd()}…`;
}

/** Shortens text to at most max characters, saying how much was cut. */
export function clip(text, max) {
  if (text.length <= max) return text;
  return `${text.slice(0, max)}… [${text.length - max} more characters cut; ask for less, e.g. a smaller radius or limit]`;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Runs one tool call against the game or the bridge. Never throws: errors come back as {error: ...}
 * so the model can read why and try something else.
 */
export async function runTool(game, name, args, { notesPath = null, maxResult = 12000 } = {}) {
  let value;
  try {
    if (name === "bridge_wait") {
      const seconds = Math.min(30, Math.max(0.1, Number(args?.seconds ?? 1) || 1));
      await sleep(seconds * 1000);
      value = { waited: seconds, events: game.takeEvents() };
    } else if (name === "bridge_note") {
      value = saveNote(notesPath, args ?? {});
    } else {
      value = await game.call(methodName(name), args ?? {});
    }
  } catch (e) {
    value = e instanceof GameError ? e.toJSON() : { error: { message: String(e?.message ?? e) } };
  }
  return clip(JSON.stringify(value ?? null), maxResult);
}

function saveNote(path, { kind = "note", title = "", text = "" }) {
  if (!path) return { saved: false, reason: "no notes file was given to the bridge (--notes)" };
  const stamp = new Date().toISOString();
  appendFileSync(path, `\n## ${kind}: ${title}\n\n_${stamp}_\n\n${text}\n`);
  return { saved: true, file: path };
}

/** The system prompt from docs/ai/system-prompt.md (the text between its --- lines). */
export function systemPrompt(job = null) {
  const text = readFileSync(join(DOCS, "system-prompt.md"), "utf8");
  const parts = text.split(/\n---\n/);
  let prompt = (parts.length >= 3 ? parts[1] : text).trim();
  if (job) prompt = prompt.replace(/Your job:[\s\S]*$/, `Your job: ${job}`);
  return prompt;
}

/** Reads `--name value` options and bare flags. */
export function options(argv, flags = []) {
  const result = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith("--")) {
      result._.push(arg);
      continue;
    }
    const key = arg.slice(2);
    if (flags.includes(key)) result[key] = true;
    else result[key] = argv[++i];
  }
  return result;
}
