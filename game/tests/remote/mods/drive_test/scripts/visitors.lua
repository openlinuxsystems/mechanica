-- A mod script: counts the players who join, on the host (RemoteTest checks it).
events.listen("players")

function on_event(topic, data)
  if data.kind == "joined" then
    state.joined = (state.joined or 0) + 1
    state.last = data.name
  end
end
