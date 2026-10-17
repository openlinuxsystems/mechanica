#!/usr/bin/env node
// Example external agent for Mechanica's AI endpoint (WebSocket JSON-RPC 2.0, docs/ai-endpoint.md).
// It builds a four-wheeled car with PlacePart, drives it forward with SetBearingTarget, turns it on the
// spot and prints where it went. Needs Node 22+ (built-in WebSocket) and a host started with --ai.
//
//   godot --path game -- --host --ai
//   node tools/ai/drive_car.mjs --mod drive_test --at 2,0.75,12
//
// Until the vanilla bearings and wheels exist (chunk 10), copy the fixture mod
// game/tests/remote/mods/drive_test into mods/ and enable it to have parts to build with.

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const url = args.find((a) => a.startsWith("ws://") || a.startsWith("wss://")) ?? "ws://127.0.0.1:27461";
const token = option("--token", process.env.MECHANICA_AI_TOKEN);
const mod = option("--mod", "drive_test");
const at = option("--at", "2,0.75,12").split(",").map(Number);
const speed = Number(option("--speed", "12")); // rad/s at the wheels
const torque = Number(option("--torque", "25")); // N·m per wheel
const turnTorque = Number(option("--turn-torque", "80")); // skid steering scrubs the wheels sideways

const socket = new WebSocket(url);
const pending = new Map();
let nextId = 0;

socket.addEventListener("message", (event) => {
  const message = JSON.parse(event.data);
  for (const response of Array.isArray(message) ? message : [message]) {
    const waiter = pending.get(response.id);
    if (!waiter) continue;
    pending.delete(response.id);
    if (response.error) waiter.reject(new Error(`${response.error.message} (${response.error.data?.code ?? response.error.code})`));
    else waiter.resolve(response.result);
  }
});

function request(method, params) {
  const id = ++nextId;
  return { message: { jsonrpc: "2.0", id, method, params }, done: new Promise((resolve, reject) => pending.set(id, { resolve, reject })) };
}

function call(method, params) {
  const { message, done } = request(method, params);
  socket.send(JSON.stringify(message));
  return done;
}

function batch(calls) {
  const requests = calls.map(([method, params]) => request(method, params));
  socket.send(JSON.stringify(requests.map((r) => r.message)));
  return Promise.all(requests.map((r) => r.done));
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function main() {
  await new Promise((resolve, reject) => {
    socket.addEventListener("open", resolve, { once: true });
    socket.addEventListener("error", () => reject(new Error(`cannot connect to ${url}`)), { once: true });
  });
  if (token) await call("mechanica.auth", { token });
  const info = await call("mechanica.info");
  console.log(`connected to ${info.game} (protocol ${info.protocol_version}); actions: ${info.actions.join(", ")}`);

  // A 4-block chassis along x, a bearing on each side of its end blocks and a wheel on each bearing.
  const { creation } = await call("PlacePart", { part: `${mod}:block`, cell: [0, 0, 0], position: at });
  const parts = [];
  for (let x = 1; x <= 3; x++) parts.push(["PlacePart", { part: `${mod}:block`, cell: [x, 0, 0], creation }]);
  const left = [];
  const right = [];
  for (const x of [0, 3]) {
    for (const side of [1, -1]) {
      const rotation = side > 0 ? "+x+z" : "+x-z";
      parts.push(["PlacePart", { part: `${mod}:bearing`, cell: [x, 0, side], rotation, creation }]);
      parts.push(["PlacePart", { part: `${mod}:wheel`, cell: [x, 0, 2 * side], rotation, creation }]);
    }
  }
  const placed = await batch(parts);
  for (let i = 0; i < placed.length; i++) {
    if (parts[i][1].part.endsWith(":bearing")) (parts[i][1].cell[2] > 0 ? left : right).push(placed[i].part);
  }
  console.log(`built creation ${creation} with ${placed.length + 1} parts`);
  await sleep(1500);

  const wheels = (l, r, max_torque = torque) =>
    batch([
      ...left.map((part) => ["SetBearingTarget", { creation, part, velocity: l, max_torque }]),
      ...right.map((part) => ["SetBearingTarget", { creation, part, velocity: r, max_torque }]),
    ]);
  const where = async (label) => {
    const state = await call("GetCreation", { creation });
    const [x, y, z] = state.bodies[0].position.map((v) => v.toFixed(2));
    const [fx, , fz] = state.bodies[0].forward;
    console.log(`${label}: at (${x}, ${y}, ${z}), heading ${((Math.atan2(-fz, fx) * 180) / Math.PI).toFixed(0)}°`);
    return state;
  };

  await where("parked");
  await wheels(speed, -speed);
  await sleep(2000);
  await wheels(0, 0);
  await sleep(1000);
  await where("after driving forward");
  await wheels(speed, speed, turnTorque);
  await sleep(1000);
  await wheels(0, 0);
  await sleep(1000);
  await where("after turning");
  await batch([...left, ...right].map((part) => ["SetBearingTarget", { creation, part, velocity: null }]));
  socket.close();
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
