#!/usr/bin/env node
// Lets any model behind an OpenAI-compatible chat completions API play and mod Mechanica: Grok served
// locally by SGLang or vLLM, llama.cpp's server, Ollama, LM Studio, or a hosted API such as xAI's.
// It gives the model the game's methods as tools (from mechanica.describe), runs the calls it makes on
// the game's AI endpoint, and feeds back results and game events until the model is done.
// Node 22+, no packages. Guide: docs/ai/connect-grok-local.md
//
//   node tools/ai/bridge/openai-bridge.mjs --base-url http://127.0.0.1:30000/v1 --model grok-2 --name Grok

import { appendFileSync, readFileSync } from "node:fs";
import { DEFAULT_URL, Game } from "./game.mjs";
import { LOCAL_TOOLS, clip, openAiTools, options, parseToolCalls, promptToolsText, runTool, systemPrompt, toolsFromDescribe } from "./tools.mjs";

const USAGE = `Usage: node tools/ai/bridge/openai-bridge.mjs --base-url URL --model NAME [options]

Model server
  --base-url URL       OpenAI-compatible API root, e.g. http://127.0.0.1:30000/v1 (SGLang),
                       http://127.0.0.1:8000/v1 (vLLM), http://127.0.0.1:8080/v1 (llama.cpp),
                       http://127.0.0.1:11434/v1 (Ollama), https://api.x.ai/v1 (xAI)
  --model NAME         Model name the server expects
  --api-key KEY        Bearer key, if the server wants one (or OPENAI_API_KEY / XAI_API_KEY)
  --tool-mode MODE     native (the API's tool calls), prompt (tools described in the prompt and called
                       with \`\`\`tool blocks, for models without tool calling) or auto (default: native,
                       falling back to prompt when the server refuses tools)
  --no-system-role     Put the instructions in the first user message (for chat templates without one)
  --temperature T      Sampling temperature (default 0.3)
  --max-tokens N       Most tokens per reply (default 2048)

Game
  --game URL           The game's AI endpoint (default ${DEFAULT_URL})
  --token TOKEN        Endpoint token (or MECHANICA_AI_TOKEN)
  --name NAME          Join the world as a player with this name (default Grok); --no-join to stay external
  --groups a,b         Only offer methods of these areas (see docs/ai/reference.md), to save context
  --topics a,b         Event topics to subscribe to (default hurt,vitals,dialogue,quest,chat,messages,raid)

Session
  --job TEXT           What the AI should do (replaces "Your job" in docs/ai/system-prompt.md)
  --prompt FILE        A whole system prompt of your own instead
  --max-steps N        Model turns before stopping (default 200)
  --stop-word WORD     Stop when the model says this with no tool call (default DONE)
  --notes FILE         Where bridge_note saves notes and reports (default ai-notes.md)
  --transcript FILE    Append every message as JSON lines
  --history N          Messages kept besides the instructions (default 60)
`;

const opts = options(process.argv.slice(2), ["no-join", "no-system-role", "help"]);
if (opts.help || !opts["base-url"] || !opts.model) {
  console.log(USAGE);
  process.exit(opts.help ? 0 : 2);
}

const baseUrl = opts["base-url"].replace(/\/+$/, "");
const apiKey = opts["api-key"] ?? process.env.OPENAI_API_KEY ?? process.env.XAI_API_KEY ?? "";
let toolMode = opts["tool-mode"] ?? "auto";
const maxSteps = Number(opts["max-steps"] ?? 200);
const stopWord = opts["stop-word"] ?? "DONE";
const notesPath = opts.notes ?? "ai-notes.md";
const history = Number(opts.history ?? 60);
const topics = (opts.topics ?? "hurt,vitals,dialogue,quest,chat,messages,raid").split(",").filter(Boolean);

const log = (text) => console.log(text);
const record = (entry) => {
  if (opts.transcript) appendFileSync(opts.transcript, JSON.stringify({ time: new Date().toISOString(), ...entry }) + "\n");
};

const game = await Game.connect(opts.game ?? DEFAULT_URL, { token: opts.token ?? process.env.MECHANICA_AI_TOKEN });
game.onClose = () => log("[bridge] the game closed the connection");
const info = await game.call("mechanica.info");
log(`[bridge] connected to ${info.game} (protocol ${info.protocol_version}) with scopes ${info.scopes.join(", ")}`);

const describe = await game.call("mechanica.describe");
const tools = [
  ...toolsFromDescribe(describe, { scopes: info.scopes, groups: opts.groups?.split(","), exclude: [] }),
  ...LOCAL_TOOLS.map((t) => ({ ...t, method: null })),
];
log(`[bridge] offering ${tools.length} tools`);

let joined = null;
if (!opts["no-join"] && info.scopes.includes("play")) {
  joined = await game.call("mechanica.join", { name: opts.name ?? "Grok" });
  log(`[bridge] joined as ${joined.name} (player ${joined.peer})`);
}
if (topics.length > 0) await game.call("mechanica.subscribe", { topics });

const instructions = opts.prompt ? readFileSync(opts.prompt, "utf8") : systemPrompt(opts.job);
const start = [
  "The session has started.",
  joined ? `You joined as ${joined.name}, player ${joined.peer}; your actions act as that player.` : "You act as an external agent on the host (not joined as a player).",
  `Subscribed to events: ${topics.join(", ") || "none"}.`,
  `Say ${stopWord} with no tool call when you have finished your job.`,
].join(" ");

const messages = [];
let refusedTurns = 0;

for (let step = 1; step <= maxSteps && !game.closed; step++) {
  const events = game.takeEvents();
  if (step === 1) messages.push({ role: "user", content: start + eventText(events) });
  else if (events.length > 0) messages.push({ role: "user", content: `Game events:${eventText(events)}` });

  const reply = await complete();
  const message = reply.message;
  let calls = (message.tool_calls ?? []).map((c) => ({ id: c.id, name: c.function?.name, arguments: parseArgs(c.function?.arguments) }));
  if (calls.length === 0) calls = parseToolCalls(message.content ?? "").map((c, i) => ({ id: `text-${step}-${i}`, ...c, fromText: true }));

  const assistant = { role: "assistant", content: message.content ?? "" };
  if (toolMode !== "prompt" && message.tool_calls?.length) assistant.tool_calls = message.tool_calls;
  messages.push(assistant);
  record({ step, role: "assistant", content: assistant.content, calls });
  if (assistant.content?.trim()) log(`[ai] ${clip(assistant.content.trim(), 600)}`);

  if (calls.length === 0) {
    if ((message.content ?? "").includes(stopWord)) {
      log("[bridge] the AI says it is done");
      break;
    }
    if (++refusedTurns >= 3) {
      log("[bridge] three replies in a row without a tool call; stopping");
      break;
    }
    messages.push({ role: "user", content: `Continue with a tool call, or say ${stopWord} if your job is finished.` });
    continue;
  }
  refusedTurns = 0;

  const textResults = [];
  for (const call of calls) {
    const known = tools.some((t) => t.name === call.name);
    const result = known
      ? await runTool(game, call.name, call.arguments, { notesPath })
      : JSON.stringify({ error: { message: `There is no tool named '${call.name}'.` } });
    log(`[tool] ${call.name} ${clip(JSON.stringify(call.arguments ?? {}), 200)} -> ${clip(result, 300)}`);
    record({ step, role: "tool", name: call.name, arguments: call.arguments, result });
    if (call.fromText || toolMode === "prompt") textResults.push(`Result of ${call.name}: ${result}`);
    else messages.push({ role: "tool", tool_call_id: call.id, content: result });
  }
  if (textResults.length > 0) messages.push({ role: "user", content: textResults.join("\n\n") });
  trimHistory();
}

game.close();
log("[bridge] finished");

// ---- helpers ----

function eventText(events) {
  if (events.length === 0) return "";
  return "\n" + events.map((e) => `- ${e.topic} (tick ${e.tick ?? "?"}): ${clip(JSON.stringify(e.data), 500)}`).join("\n");
}

function parseArgs(text) {
  if (!text) return {};
  if (typeof text === "object") return text;
  try {
    return JSON.parse(text);
  } catch {
    return {};
  }
}

function system() {
  return toolMode === "prompt" ? `${instructions}\n\n${promptToolsText(tools)}` : instructions;
}

function requestMessages() {
  const list = messages.slice();
  if (opts["no-system-role"]) {
    const first = list.findIndex((m) => m.role === "user");
    list[first] = { ...list[first], content: `${system()}\n\n${list[first].content}` };
    return toolMode === "prompt" ? list.map(flatten) : list;
  }
  const all = [{ role: "system", content: system() }, ...list];
  return toolMode === "prompt" ? all.map(flatten) : all;
}

// Without native tools, the server must not see tool roles or tool_calls.
function flatten(m) {
  if (m.role === "tool") return { role: "user", content: m.content };
  if (m.tool_calls) return { role: m.role, content: m.content };
  return m;
}

async function complete() {
  for (;;) {
    const body = {
      model: opts.model,
      messages: requestMessages(),
      temperature: Number(opts.temperature ?? 0.3),
      max_tokens: Number(opts["max-tokens"] ?? 2048),
    };
    if (toolMode !== "prompt") {
      body.tools = openAiTools(tools);
      body.tool_choice = "auto";
    }
    const response = await fetch(`${baseUrl}/chat/completions`, {
      method: "POST",
      headers: { "content-type": "application/json", ...(apiKey ? { authorization: `Bearer ${apiKey}` } : {}) },
      body: JSON.stringify(body),
    });
    const text = await response.text();
    if (!response.ok) {
      if (toolMode === "auto" && response.status >= 400 && response.status < 500) {
        log(`[bridge] the model server refused native tools (${response.status}); describing them in the prompt instead`);
        toolMode = "prompt";
        continue;
      }
      throw new Error(`model server answered ${response.status}: ${clip(text, 500)}`);
    }
    const data = JSON.parse(text);
    if (toolMode === "auto") toolMode = "native";
    return data.choices[0];
  }
}

// Keeps the newest messages, cutting only before a user turn so tool results stay with their calls.
function trimHistory() {
  if (messages.length <= history) return;
  let cut = messages.length - history;
  while (cut < messages.length && messages[cut].role !== "user") cut++;
  if (cut >= messages.length) return;
  messages.splice(0, cut);
  messages[0] = { ...messages[0], content: `(Earlier turns were cut to save space; look again with queries if you need to.)\n\n${messages[0].content}` };
}
