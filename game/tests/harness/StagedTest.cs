using System.Reflection;
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

        bool finished;
        try
        {
            finished = _stage.Tick(_stageFrames);
        }
        catch (Exception e)
        {
            // Without this an exception repeats every frame and the stage never times out.
            Check(false, $"stage '{_stage.Name}' threw {e}");
            return;
        }
        if (finished)
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

    // Clears the test's own fields, then quits through QuitCleanly.
    private void ShutDown(int code)
    {
        ForgetFields();
        QuitCleanly(this, code);
    }

    /// <summary>
    /// Frees <paramref name="test"/>'s nodes and lets .NET release its wrappers, then quits with
    /// <paramref name="code"/>. Left to engine teardown, wrappers the GC has not collected yet (sessions,
    /// ENet peers, collisions, meshes and materials) can outlive the .NET runtime, and Godot aborts on exit
    /// after the test already passed. A test that holds screens in fields clears them first.
    /// </summary>
    public static async void QuitCleanly(Node test, int code)
    {
        var tree = test.GetTree();
        foreach (var child in test.GetChildren())
            child.QueueFree();
        await test.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await test.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        for (var pass = 0; pass < 2; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await test.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        tree.Quit(code);
    }

    // Drops every object the concrete test holds in its fields (strings stay, for cleanup on exit).
    private void ForgetFields()
    {
        for (var type = GetType(); type is not null && type != typeof(StagedTest); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.FieldType.IsValueType && field.FieldType != typeof(string))
                    field.SetValue(this, null);
            }
        }
    }
}
