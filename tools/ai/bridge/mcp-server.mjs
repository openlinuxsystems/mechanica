#!/usr/bin/env node
// An MCP server (stdio) that gives Claude, or any MCP client, Mechanica's AI endpoint as tools: every
// method from mechanica.describe, plus game_events and game_wait for pushed events, and the AI docs as
// resources. Node 22+, no packages. Guide: docs/ai/connect-claude.md
//
//   claude mcp add mechanica -- node /path/to/mechanica/tools/ai/bridge/mcp-server.mjs --name Claude

import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { DEFAULT_URL, Game } from "./game.mjs";
import { DOCS, LOCAL_TOOLS, mcpTools, options, runTool, systemPrompt, toolsFromDescribe } from "./tools.mjs";

const PROTOCOL_VERSIONS = ["2025-06-18", "2025-03-26", "2024-11-05"];
const opts = options(process.argv.slice(2), ["help"]);
if (opts.help) {
  console.error(`Usage: node tools/ai/bridge/mcp-server.mjs [--game URL] [--token TOKEN] [--name NAME] [--topics a,b] [--groups a,b] [--notes FILE]

  --game URL     The game's AI endpoint (default ${DEFAULT_URL})
  --token TOKEN  Endpoint token (or MECHANICA_AI_TOKEN)
  --name NAME    Join as a player with this name as soon as the game is reached (otherwise call mechanica__join)
  --topics a,b   Event topics to subscribe to (default hurt,vitals,dialogue,quest,chat,messages,raid)
  --groups a,b   Only offer methods of these areas, to save context
  --notes FILE   Where bridge_note saves notes and reports (default ai-notes.md)`);
  process.exit(0);
}

const url = opts.game ?? DEFAULT_URL;
const token = opts.token ?? process.env.MECHANICA_AI_TOKEN;
const topics = (opts.topics ?? "hurt,vitals,dialogue,quest,chat,messages,raid").split(",").filter(Boolean);
const notesPath = opts.notes ?? "ai-notes.md";
const warn = (text) => console.error(`[mechanica-mcp] ${text}`);

const EVENT_TOOLS = [
  {
    name: "game_events",
    description: "Returns the game events (damage, health, dialogue, quests, chat, script messages, raids, ...) pushed since the last call, oldest first.",
    parameters: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "game_wait",
    description: "Waits a number of seconds of real time (for walking, machines, crops, raids) and returns the game events that arrived meanwhile.",
    parameters: {
      type: "object",
      properties: { seconds: { type: "number", description: "How long to wait, 0.1 to 30 s.", default: 1 } },
      additionalProperties: false,
    },
  },
];

let game = null;
let tools = null;
let connecting = null;

// Connects on first use and again after the game closes, so the client can start before the game.
async function ensureGame() {
  if (game && !game.closed) return game;
  connecting ??= (async () => {
    try {
      const g = await Game.connect(url, { token });
      const info = await g.call("mechanica.info");
      const describe = await g.call("mechanica.describe");
      tools = [
        ...toolsFromDescribe(describe, { scopes: info.scopes, groups: opts.groups?.split(",") }),
        ...EVENT_TOOLS,
        LOCAL_TOOLS.find((t) => t.name === "bridge_note"),
      ];
      if (opts.name && info.scopes.includes("play")) {
        const joined = await g.call("mechanica.join", { name: opts.name });
        warn(`joined as ${joined.name} (player ${joined.peer})`);
      }
      if (topics.length > 0) await g.call("mechanica.subscribe", { topics });
      g.onClose = () => warn("the game closed the connection");
      game = g;
      warn(`connected to ${url} with scopes ${info.scopes.join(", ")}; ${tools.length} tools`);
      return g;
    } finally {
      connecting = null;
    }
  })();
  return connecting;
}

async function callTool(name, args) {
  const g = await ensureGame();
  if (!tools.some((t) => t.name === name)) throw Object.assign(new Error(`Unknown tool: ${name}`), { code: -32602 });
  if (name === "game_events") return JSON.stringify({ events: g.takeEvents() });
  if (name === "game_wait") return runTool(g, "bridge_wait", args);
  return runTool(g, name, args, { notesPath });
}

function resources() {
  return readdirSync(DOCS)
    .filter((f) => f.endsWith(".md") || f.endsWith(".json"))
    .sort()
    .map((f) => ({
      uri: `mechanica://docs/ai/${f}`,
      name: f,
      mimeType: f.endsWith(".json") ? "application/json" : "text/markdown",
      description: `Mechanica AI docs: ${f}`,
    }));
}

async function handle(request) {
  const { method, params } = request;
  switch (method) {
    case "initialize":
      return {
        protocolVersion: PROTOCOL_VERSIONS.includes(params?.protocolVersion) ? params.protocolVersion : PROTOCOL_VERSIONS[0],
        capabilities: { tools: { listChanged: false }, resources: { listChanged: false } },
        serverInfo: { name: "mechanica", version: "1.0.0" },
        instructions: systemPrompt(),
      };
    case "ping":
      return {};
    case "tools/list":
      await ensureGame();
      return { tools: mcpTools(tools) };
    case "tools/call": {
      let text;
      try {
        text = await callTool(params?.name, params?.arguments ?? {});
      } catch (e) {
        if (e.code === -32602) throw e;
        text = JSON.stringify({ error: { message: String(e.message ?? e) } });
      }
      return { content: [{ type: "text", text }], isError: text.startsWith('{"error"') };
    }
    case "resources/list":
      return { resources: resources() };
    case "resources/read": {
      const resource = resources().find((r) => r.uri === params?.uri);
      if (!resource) throw Object.assign(new Error(`Unknown resource: ${params?.uri}`), { code: -32602 });
      return { contents: [{ uri: resource.uri, mimeType: resource.mimeType, text: readFileSync(join(DOCS, resource.name), "utf8") }] };
    }
    case "prompts/list":
      return { prompts: [] };
    default:
      throw Object.assign(new Error(`Method not found: ${method}`), { code: -32601 });
  }
}

const send = (message) => process.stdout.write(JSON.stringify(message) + "\n");

createInterface({ input: process.stdin, crlfDelay: Infinity }).on("line", async (line) => {
  if (!line.trim()) return;
  let request;
  try {
    request = JSON.parse(line);
  } catch {
    send({ jsonrpc: "2.0", id: null, error: { code: -32700, message: "Parse error" } });
    return;
  }
  if (request.id === undefined) return; // notifications (initialized, cancelled) need no answer
  try {
    send({ jsonrpc: "2.0", id: request.id, result: await handle(request) });
  } catch (e) {
    send({ jsonrpc: "2.0", id: request.id, error: { code: e.code ?? -32603, message: String(e.message ?? e) } });
  }
});

process.stdin.on("end", () => {
  game?.close();
  process.exit(0);
});
