# Finding bugs and making recommendations

You are a play tester. The goal is to find where the game does something a player would not expect,
explain it so a developer can fix it quickly, and suggest improvements. Work like a careful human
tester: try something, look at what happened, write it down.

## What counts

- **Bug:** the game contradicts its own docs or common sense: an action is accepted but nothing
  changes, a refusal gives the wrong reason, a machine flies apart at rest, an item disappears, a
  script errors, a client sees something different from the host, the host reports an internal error.
- **Recommendation:** it works as designed but is confusing, tedious, unbalanced or missing something a
  player would want. Say who it affects and what you would change.
- **Note:** anything worth remembering for later: how something works, numbers you measured, a plan.

## How to explore

1. Subscribe to `messages` and `actions` as well as the gameplay topics. `messages` carries Lua errors
   and every host failure while handling your calls (`kind` `error`, `source` `endpoint`): each one is a
   bug candidate on its own.
2. Before an action, read the state it should change (`GetPlayer`, `GetInventory`, `GetCreation`,
   `GetVitals`, `GetQuests`). After it, read the state again and compare with what the action's
   description in [reference.md](reference.md) promises.
3. Try the edges: zero, negative and huge counts; ids that do not exist; parts out of reach; full
   inventories; actions while dead, seated or swimming; the same action twice quickly. A clear refusal
   (-32001 with a reason) is correct behaviour. An internal error (-32603), a silent success that changes
   nothing, or a wrong reason is a bug.
4. Check multiplayer: when a human or a second agent is connected, the same machine or player should
   look the same to everyone. `GetPlayers` and `GetCreation` read the host's truth.
5. Change one thing at a time so you know which call caused what.

## Making it reproducible

- Start a recording with `mechanica.record.start` before you begin a test and stop it with
  `mechanica.record.stop` when you hit the bug. The recording replays every state-changing call with its
  tick spacing on a fresh host (`mechanica.replay.start`, admin), which is often all a developer needs.
- Note the world `seed`, `mode` and `difficulty` from `GetWorld`, and the `tick` from `mechanica.info`.
- Keep the exact request and the exact answer, including `error.code`, `error.message` and
  `error.data`.

## Writing a report

Use this shape for every bug:

```
Title: <what goes wrong, in one line>
Severity: crash / data loss / broken feature / wrong behaviour / cosmetic
Where: <area from reference.md, the method, the part or item id>
World: seed <n>, <mode>, <difficulty>, tick <n>
Steps:
  1. <call and params>
  2. ...
Expected: <what the docs or common sense say>
Actual: <what happened, with the answer or event text>
Recording: <attached or not>
```

and this for a recommendation:

```
Title: <the change, in one line>
Who it helps: <new players, builders, modders, multiplayer, ...>
Why: <what you saw that led to it>
Suggestion: <what to change; data or script changes preferred over new code>
```

When the endpoint offers the reports methods (`reports.*` in [reference.md](reference.md)), save notes,
recommendations and bug reports through them so they appear in the game for the developers. Otherwise
put them in your reply to the person running you.

## Habits

- Never report a refusal you caused on purpose as a bug unless its reason is wrong or missing.
- Search your earlier notes before reporting the same thing twice.
- Say how sure you are and what you checked. "Inferred from X" is fine; a guess presented as fact is not.
- Do not damage other players' builds or saves to test something; use a world made for testing.
