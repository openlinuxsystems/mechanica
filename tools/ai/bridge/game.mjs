// A connection to Mechanica's AI endpoint (JSON-RPC 2.0 over WebSocket, docs/ai-endpoint.md).
// Node 22+ (built-in WebSocket); no packages.

export const DEFAULT_URL = "ws://127.0.0.1:27461";

/** A JSON-RPC error answer from the game. */
export class GameError extends Error {
  constructor(error) {
    super(error.message);
    this.code = error.code;
    this.data = error.data;
  }

  toJSON() {
    return { error: { code: this.code, message: this.message, data: this.data } };
  }
}

/** One open connection: calls, and the events the game pushed since they were last taken. */
export class Game {
  #socket;
  #pending = new Map();
  #nextId = 0;
  #events = [];
  #closed = false;

  /** Max events kept between takes; older ones are dropped and counted. */
  maxEvents = 500;
  dropped = 0;

  /** Called with each event as it arrives, besides being kept for takeEvents(). */
  onEvent = null;
  /** Called once when the connection closes. */
  onClose = null;

  constructor(socket) {
    this.#socket = socket;
    socket.addEventListener("message", (event) => this.#receive(event.data));
    socket.addEventListener("close", () => {
      this.#closed = true;
      for (const waiter of this.#pending.values()) waiter.reject(new Error("the game closed the connection"));
      this.#pending.clear();
      this.onClose?.();
    });
  }

  /** Opens a connection, authenticating when a token is given. */
  static async connect(url = DEFAULT_URL, { token } = {}) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      socket.addEventListener("open", resolve, { once: true });
      socket.addEventListener("error", () => reject(new Error(`cannot connect to the game at ${url}; is it hosting with --ai?`)), { once: true });
    });
    const game = new Game(socket);
    if (token) await game.call("mechanica.auth", { token });
    return game;
  }

  get closed() {
    return this.#closed;
  }

  /** Calls a method; resolves with its result or rejects with a GameError. */
  call(method, params = {}) {
    if (this.#closed) return Promise.reject(new Error("the connection to the game is closed"));
    const id = ++this.#nextId;
    const done = new Promise((resolve, reject) => this.#pending.set(id, { resolve, reject }));
    this.#socket.send(JSON.stringify({ jsonrpc: "2.0", id, method, params }));
    return done;
  }

  /** Takes the events received since the last take, oldest first. */
  takeEvents() {
    const events = this.#events;
    this.#events = [];
    if (this.dropped > 0) {
      events.unshift({ topic: "events_dropped", data: { count: this.dropped } });
      this.dropped = 0;
    }
    return events;
  }

  close() {
    this.#socket.close();
  }

  #receive(text) {
    let message;
    try {
      message = JSON.parse(text);
    } catch {
      return;
    }
    for (const item of Array.isArray(message) ? message : [message]) {
      if (item.method === "mechanica.event" && item.params) {
        this.#events.push(item.params);
        if (this.#events.length > this.maxEvents) {
          this.#events.shift();
          this.dropped++;
        }
        this.onEvent?.(item.params);
        continue;
      }
      const waiter = this.#pending.get(item.id);
      if (!waiter) continue;
      this.#pending.delete(item.id);
      if (item.error) waiter.reject(new GameError(item.error));
      else waiter.resolve(item.result ?? null);
    }
  }
}
