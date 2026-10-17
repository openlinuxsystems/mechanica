using Godot;

namespace Mechanica.Game.Tests;

/// <summary>
/// Base for headless test scenes that run as a list of stages, one physics tick at a time. A stage's
/// tick function gets its frame count and returns true when the stage is done; a stage that takes
/// longer than <see cref="StageTimeoutFrames"/> fails. The scene quits with 0 when every stage passed
/// and 1 on the first failed <see cref="Check"/>.
/// </summary>
public abstract partial class StagedTest : Node
{
    private readonly Queue<Stage> _stages = new();
    private Stage? _stage;
    private int _stageFrames;
    private bool _done;

    private sealed record Stage(string Name, Func<int, bool> Tick);

    /// <summary>Frames a stage may take.</summary>
    protected virtual int StageTimeoutFrames => 600;

    /// <summary>Prefix for log lines, e.g. <c>net</c>.</summary>
    protected abstract string Tag { get; }

    /// <summary>True once the test passed or failed.</summary>
    protected bool Done => _done;

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
            return;
        if (_stage is null)
        {
            if (!_stages.TryDequeue(out _stage))
            {
                Finish(0);
                return;
            }
            _stageFrames = 0;
        }

        if (_stage.Tick(_stageFrames))
        {
            GD.Print($"[{Tag}] done: {_stage.Name} ({_stageFrames} frames)");
            _stage = null;
        }
        else if (++_stageFrames > StageTimeoutFrames)
        {
            Check(false, $"stage '{_stage.Name}' timed out");
        }
    }

    /// <summary>Queues a stage.</summary>
    protected void Add(string name, Func<int, bool> tick) => _stages.Enqueue(new Stage(name, tick));

    /// <summary>Queues a stage that waits <paramref name="frames"/> frames.</summary>
    protected void Wait(string name, int frames) => Add(name, frame => frame >= frames);

    /// <summary>Logs a check and fails the test if it is false. Returns <paramref name="ok"/>.</summary>
    protected bool Check(bool ok, string what)
    {
        GD.Print($"[{Tag}] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok)
            Finish(1);
        return ok;
    }

    /// <summary>Ends the test with an exit code.</summary>
    protected void Finish(int code)
    {
        if (_done)
            return;
        _done = true;
        GD.Print($"[{Tag}] {(code == 0 ? "OK" : "FAILED")}");
        ShutDown(code);
    }

    // Frees the test's nodes and lets .NET release its wrappers before quitting. Left to engine
    // teardown, wrappers the GC has not collected yet (sessions, ENet peers, collisions) can outlive
    // the .NET runtime, and Godot aborts on exit after the test already passed.
    private async void ShutDown(int code)
    {
        foreach (var child in GetChildren())
            child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GetTree().Quit(code);
    }
}
