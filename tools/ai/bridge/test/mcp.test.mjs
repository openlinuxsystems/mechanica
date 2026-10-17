// The MCP server answers the handshake, lists the AI docs as resources and reports an unreachable game
// as a tool error instead of failing.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const server = fileURLToPath(new URL("../mcp-server.mjs", import.meta.url));

function start(args) {
  const child = spawn(process.execPath, [server, ...args], { stdio: ["pipe", "pipe", "pipe"] });
  const waiting = new Map();
  createInterface({ input: child.stdout }).on("line", (line) => {
    const message = JSON.parse(line);
    waiting.get(message.id)?.(message);
  });
  let id = 0;
  const request = (method, params = {}) =>
    new Promise((resolve) => {
      waiting.set(++id, resolve);
      child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
    });
  return { child, request, notify: (method) => child.stdin.write(JSON.stringify({ jsonrpc: "2.0", method }) + "\n") };
}

test("handshake, resources and an unreachable game", async () => {
  const { child, request, notify } = start(["--game", "ws://127.0.0.1:9"]);
  try {
    const init = await request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "test", version: "1" } });
    assert.equal(init.result.protocolVersion, "2025-06-18");
    assert.equal(init.result.serverInfo.name, "mechanica");
    assert.match(init.result.instructions, /You are playing Mechanica/);
    notify("notifications/initialized");
    assert.deepEqual((await request("ping")).result, {});

    const list = await request("resources/list");
    const uris = list.result.resources.map((r) => r.uri);
    assert.ok(uris.includes("mechanica://docs/ai/playing.md"));
    assert.ok(uris.includes("mechanica://docs/ai/describe.json"));
    const read = await request("resources/read", { uri: "mechanica://docs/ai/playing.md" });
    assert.match(read.result.contents[0].text, /# Playing Mechanica as an AI/);

    const call = await request("tools/call", { name: "GetPlayer", arguments: {} });
    assert.equal(call.result.isError, true);
    assert.match(call.result.content[0].text, /cannot connect to the game/);
    assert.equal((await request("nope/nothing")).error.code, -32601);
  } finally {
    child.kill();
  }
});
