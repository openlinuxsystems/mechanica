using System.Text.Json;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Saves;

/// <summary>
/// The endpoint's <c>world.*</c> methods (AUDIT §5 PR 5), all admin: the running world's status, saving,
/// listing saves, loading one, starting a new world or the same one afresh, and the time scale and pause
/// for tests. Loads and new worlds answer at once; the world changes after, and the endpoint and its
/// connections carry over to it. Subscribe to the <c>world</c> topic to hear when it has loaded.
/// </summary>
public sealed class WorldModule(WorldControl control) : IRpcModule
{
    /// <inheritdoc/>
    public IEnumerable<RpcMethodInfo> Methods =>
    [
        RpcMethod.Create<WorldNoParams>("world.info", AgentScope.Admin,
            "The running world: name, seed, mode, difficulty, the save it came from, world time, tick, time scale, paused, joined players, and whether a world change is under way.",
            (_, _) => Run(() => RpcMethod.Value(control.Info()))),
        RpcMethod.Create<SaveWorldParams>("world.save", AgentScope.Admin,
            "Save the world, under its own name or a new one. Returns the name, file and path.",
            (_, p) => Run(() => RpcMethod.Value(control.Save(p)))),
        RpcMethod.Create<WorldNoParams>("world.list", AgentScope.Admin,
            "Saved worlds, newest first: name, file, saved_at, mode, play_seconds, and error for a save that does not load.",
            (_, _) => Run(() => RpcMethod.Value(control.List()))),
        RpcMethod.Create<LoadWorldParams>("world.load", AgentScope.Admin,
            "Load a saved world by name or file name. Answers at once; the world loads after, and the connection carries over. Players joined are disconnected, so it needs disconnect_clients when there are any.",
            (_, p) => Run(() => RpcMethod.Value(control.Load(p)))),
        RpcMethod.Create<NewWorldParams>("world.new", AgentScope.Admin,
            "Start a new world (name, seed, mode, difficulty, character). Answers at once with what it will be; the world starts after.",
            (_, p) => Run(() => RpcMethod.Value(control.New(p)))),
        RpcMethod.Create<ResetWorldParams>("world.reset", AgentScope.Admin,
            "Start the running world again from nothing: the same name, seed, mode and difficulty, nothing built. For a clean test world.",
            (_, p) => Run(() => RpcMethod.Value(control.Reset(p)))),
        RpcMethod.Create<TimeScaleParams>("world.time_scale", AgentScope.Admin,
            $"Run the world faster or slower ({WorldControl.MinTimeScale} to {WorldControl.MaxTimeScale}, 1 is real time). Returns the status.",
            (_, p) => Run(() => RpcMethod.Value(control.SetTimeScale(p)))),
        RpcMethod.Create<PauseParams>("world.pause", AgentScope.Admin,
            "Pause or resume the world. The endpoint keeps answering while paused. Returns the status.",
            (_, p) => Run(() => RpcMethod.Value(control.SetPaused(p)))),
    ];

    private static JsonElement? Run(Func<JsonElement> call)
    {
        try
        {
            return call();
        }
        catch (WorldControlException e)
        {
            throw new RpcException(e.Refusal switch
            {
                WorldRefusal.BadParams => RpcErrors.BadParams,
                WorldRefusal.Busy => RpcErrors.Busy,
                _ => RpcErrors.Rejected,
            }, e.Message);
        }
    }
}
