-- Empty: every hook, ready to fill in
-- {{name}}. See the Lua API Reference (docs/lua-api.md) for what scripts can call.

function on_create()
  -- Runs once when the part starts. Keep anything that must survive a hot reload in state.
end

function on_update(dt)
  -- Runs every physics step (dt seconds) while the Creation is simulated.
end

function on_reload()
  -- Runs after a hot reload instead of on_create; state is kept.
end

function on_destroy()
  -- Runs when the part is removed.
end
