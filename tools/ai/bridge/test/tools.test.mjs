// node --test tools/ai/bridge/test
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { DOCS, clip, methodName, openAiTools, options, parseToolCalls, promptToolsText, systemPrompt, toolName, toolsFromDescribe } from "../tools.mjs";

const describe = JSON.parse(readFileSync(join(DOCS, "describe.json"), "utf8"));

test("method names round-trip through tool names that APIs accept", () => {
  for (const m of describe.methods) {
    const name = toolName(m.name);
    assert.match(name, /^[A-Za-z0-9_-]{1,64}$/);
    assert.equal(methodName(name), m.name);
  }
});

test("tools follow the connection's scopes and the chosen groups", () => {
  const all = toolsFromDescribe(describe);
  assert.equal(all.length, describe.methods.length - 1, "everything but mechanica.auth");
  const play = toolsFromDescribe(describe, { scopes: ["observe", "play"] });
  assert.ok(play.every((t) => t.scope !== "admin"));
  assert.ok(play.length < all.length);
  const survival = toolsFromDescribe(describe, { groups: ["survival"] });
  assert.ok(survival.every((t) => t.group === "survival" || t.group === "mechanica"));
  assert.ok(survival.some((t) => t.method === "PickUp"));
  assert.ok(survival.some((t) => t.name === "mechanica__join"));
  const fn = openAiTools(play)[0];
  assert.equal(fn.type, "function");
  assert.equal(fn.function.parameters.type, "object");
});

test("tool calls are read from tool blocks, json blocks and bare objects", () => {
  assert.deepEqual(parseToolCalls('Look first.\n```tool\n{"name": "GetPlayer", "arguments": {}}\n```'), [{ name: "GetPlayer", arguments: {} }]);
  assert.deepEqual(
    parseToolCalls('```json\n[{"name": "A", "arguments": {"x": 1}}, {"tool": "B", "args": {"y": "}"}}]\n```'),
    [{ name: "A", arguments: { x: 1 } }, { name: "B", arguments: { y: "}" } }],
  );
  assert.deepEqual(parseToolCalls('I will call {"name": "Raycast", "arguments": {"range": 5}} now.'), [{ name: "Raycast", arguments: { range: 5 } }]);
  assert.deepEqual(parseToolCalls("Nothing to do. DONE"), []);
  assert.deepEqual(parseToolCalls('```tool\nnot json\n```'), []);
});

test("the prompt lists every tool with its params", () => {
  const tools = toolsFromDescribe(describe, { scopes: ["observe", "play"] });
  const text = promptToolsText(tools);
  for (const t of tools) assert.ok(text.includes(`- ${t.name}(`), t.name);
  assert.match(text, /- SetPlayerInput\(move_x\?: number, move_z\?: number, yaw\?: number\|null/);
  assert.match(text, /```tool/);
});

test("helpers", () => {
  assert.equal(clip("abc", 5), "abc");
  assert.match(clip("abcdefgh", 3), /^abc… \[5 more characters cut/);
  const prompt = systemPrompt();
  assert.match(prompt, /^You are playing Mechanica/);
  assert.doesNotMatch(prompt, /---/);
  assert.match(systemPrompt("Build a car."), /Your job: Build a car\.$/);
  assert.deepEqual(options(["--model", "m", "--no-join", "x"], ["no-join"]), { _: ["x"], model: "m", "no-join": true });
});
