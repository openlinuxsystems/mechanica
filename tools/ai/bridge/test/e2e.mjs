// End-to-end run of both bridges against a real game host (started by game/tests/aibridge). The MCP
// server is driven like Claude drives it; the OpenAI-compatible bridge talks to a scripted fake model
// server, once with native tool calls and once with tools described in the prompt (as for a local Grok
// without a tool-call parser). Writes {ok, checks: [{ok, what}]} to --out.
//
//   node tools/ai/bridge/test/e2e.mjs --game ws://127.0.0.1:PORT --token TOKEN --out result.json

import { spawn } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";
import { options } from "../tools.mjs";

const opts = options(process.argv.slice(2));
const bridgeDir = fileURLToPath(new URL("..", import.meta.url));
const work = mkdtempSync(join(tmpdir(), "mechanica-bridge-"));
const checks = [];
const check = (ok, what) => {
  checks.push({ ok: Boolean(ok), what });
  console.log(`[e2e] ${ok ? "PASS" : "FAIL"}: ${what}`);
};

function run(script, args, { input = false } = {}) {
  const child = spawn(process.execPath, [join(bridgeDir, script), ...args], { stdio: [input ? "pipe" : "ignore", "pipe", "pipe"] });
  let out = "";
  child.stdout.on("data", (d) => (out += d));
  child.stderr.on("data", (d) => process.stderr.write(d));
  const done = new Promise((resolve) => child.on("exit", (code) => resolve({ code, out })));
  return { child, done, output: () => out };
}

// ---- MCP, as Claude uses it ----

async function mcp() {
  const server = run("mcp-server.mjs", ["--game", opts.game, "--token", opts.token, "--name", "Mcp", "--notes", join(work, "mcp-notes.md")], { input: true });
  const waiting = new Map();
  createInterface({ input: server.child.stdout }).on("line", (line) => {
    const message = JSON.parse(line);
    waiting.get(message.id)?.(message);
  });
  let id = 0;
  const request = (method, params = {}) =>
    new Promise((resolve) => {
      waiting.set(++id, resolve);
      server.child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
    });
  const tool = async (name, args = {}) => {
    const answer = await request("tools/call", { name, arguments: args });
    return { error: answer.result?.isError, value: JSON.parse(answer.result?.content?.[0]?.text ?? "null") };
  };

  const init = await request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "e2e", version: "1" } });
  check(init.result?.serverInfo?.name === "mechanica", "MCP: initialize answers");
  server.child.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }) + "\n");
  const list = await request("tools/list");
  const names = (list.result?.tools ?? []).map((t) => t.name);
  check(["GetPlayer", "SetPlayerInput", "mechanica__join", "game_wait", "game_events", "bridge_note"].every((n) => names.includes(n)), `MCP: tools/list offers the game's methods (${names.length})`);
  check(!names.includes("mechanica__auth"), "MCP: but not mechanica.auth");
  const info = await tool("mechanica__info");
  check(info.value?.source === "player", `MCP: --name joined it as a player (${JSON.stringify(info.value?.source)})`);
  // A player that has only just joined may still be dropping onto the ground; walk once it stands.
  let before = await tool("GetPlayer");
  for (let look = 0; look < 40 && !before.value?.on_floor; look++) {
    await tool("game_wait", { seconds: 0.25 });
    before = await tool("GetPlayer");
  }
  await tool("SetPlayerInput", { move_z: 1, yaw: 0, ticks: 45 });
  const waited = await tool("game_wait", { seconds: 1.2 });
  check(Array.isArray(waited.value?.events), "MCP: game_wait returns events");
  // The 45 ticks of input run on the game's clock, which a busy machine can run behind real time, so look again
  // until the walk is done rather than only after a fixed wait.
  let moved = 0;
  for (let look = 0; look < 20; look++) {
    const after = await tool("GetPlayer");
    moved = before.value.position[2] - after.value.position[2];
    if (moved > 0.8) break;
    await tool("game_wait", { seconds: 0.5 });
  }
  check(moved > 0.8, `MCP: the player walked down -z (${moved.toFixed(2)} m)`);
  const refused = await tool("SetPlayerInput", { move_z: 1, ticks: 100000 });
  check(refused.error === true && /ticks/.test(refused.value?.error?.message), `MCP: a refused call comes back as a tool error (${JSON.stringify(refused.value)})`);
  const unknown = await request("tools/call", { name: "NoSuchTool", arguments: {} });
  check(unknown.error?.code === -32602, "MCP: an unknown tool is a protocol error");
  server.child.stdin.end();
  await server.done;
}

// ---- OpenAI-compatible bridge against a scripted model server ----

function fakeModel(script) {
  const requests = [];
  const server = createServer((req, res) => {
    let body = "";
    req.on("data", (d) => (body += d));
    req.on("end", () => {
      const parsed = JSON.parse(body);
      requests.push(parsed);
      const step = script(parsed, requests.length);
      res.writeHead(step.status ?? 200, { "content-type": "application/json" });
      res.end(JSON.stringify(step.status ? { error: { message: step.error } } : { choices: [{ index: 0, message: step.message, finish_reason: "stop" }] }));
    });
  });
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve({ server, requests, url: `http://127.0.0.1:${server.address().port}/v1` })));
}

const nativeCall = (id, name, args) => ({ id, type: "function", function: { name, arguments: JSON.stringify(args) } });

async function openaiNative() {
  const turns = [
    { role: "assistant", content: "", tool_calls: [nativeCall("a", "GetPlayer", {})] },
    { role: "assistant", content: "Walking north.", tool_calls: [nativeCall("b", "SetPlayerInput", { move_z: 1, yaw: 0, ticks: 45 }), nativeCall("c", "bridge_wait", { seconds: 1.2 })] },
    { role: "assistant", content: "", tool_calls: [nativeCall("d", "GetPlayer", {}), nativeCall("e", "bridge_note", { kind: "bug", title: "test note", text: "written by the e2e test" })] },
    { role: "assistant", content: "All checked. DONE" },
  ];
  const model = await fakeModel((_, n) => ({ message: turns[Math.min(n, turns.length) - 1] }));
  const notes = join(work, "native-notes.md");
  const transcript = join(work, "native.jsonl");
  const bridge = run("openai-bridge.mjs", ["--base-url", model.url, "--model", "fake", "--game", opts.game, "--token", opts.token, "--name", "Grok", "--notes", notes, "--transcript", transcript, "--max-steps", "8"]);
  const { code } = await bridge.done;
  model.server.close();
  check(code === 0, `native: the bridge exits cleanly (${code})`);
  const first = model.requests[0];
  check(first?.messages?.[0]?.role === "system" && /You are playing Mechanica/.test(first.messages[0].content), "native: the system prompt comes from docs/ai/system-prompt.md");
  const offered = (first?.tools ?? []).map((t) => t.function.name);
  check(offered.includes("SetPlayerInput") && offered.includes("bridge_wait"), `native: the game's methods are offered as tools (${offered.length})`);
  const results = model.requests.at(-1)?.messages?.filter((m) => m.role === "tool") ?? [];
  check(results.length === 5, `native: every tool result went back to the model (${results.length})`);
  const positions = results.map((m) => JSON.parse(m.content)).filter((v) => Array.isArray(v?.position)).map((v) => v.position);
  const moved = positions.length === 2 ? positions[0][2] - positions[1][2] : 0;
  check(moved > 0.8, `native: the player walked down -z (${moved.toFixed(2)} m)`);
  check(/## bug: test note/.test(readFileSync(notes, "utf8")), "native: bridge_note saved the report");
  check(model.requests.length === 4, `native: it stopped at the stop word (${model.requests.length} model calls)`);
  check(readFileSync(transcript, "utf8").trim().split("\n").length >= 9, "native: the transcript has every turn");
}

async function openaiPrompt() {
  const replies = [
    'First I look.\n```tool\n{"name": "GetPlayer", "arguments": {}}\n```',
    '```tool\n{"name": "SetPlayerInput", "arguments": {"move_z": 1, "yaw": 0, "ticks": 45}}\n```\n```tool\n{"name": "bridge_wait", "arguments": {"seconds": 1.2}}\n```',
    '{"name": "GetPlayer", "arguments": {}}',
    "Finished. DONE",
  ];
  let answered = 0;
  const model = await fakeModel((request) => {
    if (request.tools) return { status: 400, error: "tools are not supported by this model" };
    return { message: { role: "assistant", content: replies[Math.min(++answered, replies.length) - 1] } };
  });
  const bridge = run("openai-bridge.mjs", ["--base-url", model.url, "--model", "grok-2", "--game", opts.game, "--token", opts.token, "--name", "Grok2", "--no-system-role", "--max-steps", "8"]);
  const { code, out } = await bridge.done;
  model.server.close();
  check(code === 0, `prompt: the bridge exits cleanly (${code})`);
  check(model.requests[0]?.tools && !model.requests[1]?.tools, "prompt: after the server refused tools, it described them in the prompt");
  check(/describing them in the prompt/.test(out), "prompt: and said so");
  const firstPrompt = model.requests[1]?.messages ?? [];
  check(firstPrompt.every((m) => m.role !== "system") && /```tool/.test(firstPrompt[0]?.content) && /- SetPlayerInput\(/.test(firstPrompt[0]?.content),
    "prompt: the instructions and tool list are in the first user message (--no-system-role)");
  const last = model.requests.at(-1)?.messages ?? [];
  const positions = last
    .filter((m) => m.role === "user" && m.content.startsWith("Result of GetPlayer"))
    .map((m) => JSON.parse(m.content.slice(m.content.indexOf(":") + 1)).position);
  const moved = positions.length === 2 ? Math.hypot(positions[1][0] - positions[0][0], positions[1][2] - positions[0][2]) : 0;
  check(moved > 0.8, `prompt: the player walked (${moved.toFixed(2)} m)`);
  check(last.every((m) => m.role !== "tool" && !m.tool_calls), "prompt: no tool roles were sent to a server without tools");
}

try {
  await mcp();
  await openaiNative();
  await openaiPrompt();
} catch (e) {
  check(false, `the run finished without throwing (${e.stack ?? e})`);
}
writeFileSync(opts.out, JSON.stringify({ ok: checks.length > 0 && checks.every((c) => c.ok), checks }, null, 2));
process.exit(0);
