using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// The timer library: timer.after, timer.every and timer.cancel for mod and part scripts. Timers count simulated
// seconds, fire at the start of a step (before on_update) and end when the script reloads or stops.
public sealed partial class ScriptRuntime
{
    private sealed class Timer(int id, double remaining, double interval, DynValue function)
    {
        public int Id { get; } = id;
        public double Remaining { get; set; } = remaining;
        // Seconds between repeats (timer.every); 0 fires once.
        public double Interval { get; } = interval;
        public DynValue Function { get; } = function;
        public bool Cancelled { get; set; }
    }

    private void AddTimerLibrary(Script script)
    {
        var timer = new Table(script);
        Fn(timer, "after", args => DynValue.NewNumber(AddTimer(args, "timer.after", repeat: false)));
        Fn(timer, "every", args => DynValue.NewNumber(AddTimer(args, "timer.every", repeat: true)));
        Fn(timer, "cancel", args =>
        {
            var instance = Listener("timer.cancel");
            var id = (int)args.AsType(0, "timer.cancel", DataType.Number).Number;
            var found = instance.Timers.FirstOrDefault(t => t.Id == id && !t.Cancelled);
            if (found is null)
                return DynValue.False;
            found.Cancelled = true;
            return DynValue.True;
        });
        Fn(timer, "count", _ => DynValue.NewNumber(Listener("timer.count").Timers.Count(t => !t.Cancelled)));
        script.Globals.Set("timer", DynValue.NewTable(timer));
    }

    // Item scripts live only while the item is used, so timers belong to mod and part scripts (Listener).
    private int AddTimer(CallbackArguments args, string function, bool repeat)
    {
        var instance = Listener(function);
        var seconds = args.AsType(0, function, DataType.Number).Number;
        var callback = args.AsType(1, function, DataType.Function);
        if (!double.IsFinite(seconds) || seconds < 0 || (repeat && seconds <= 0))
            throw new ScriptRuntimeException($"{function}: seconds must be {(repeat ? "more than 0" : "0 or more")} (got {seconds})");
        if (instance.Timers.Count(t => !t.Cancelled) >= _settings.MaxTimersPerScript)
            throw new ScriptRuntimeException($"{function}: a script may run at most {_settings.MaxTimersPerScript} timers at once");
        var timer = new Timer(++instance.LastTimer, seconds, repeat ? seconds : 0, callback);
        instance.Timers.Add(timer);
        return timer.Id;
    }

    // Counts down each timer by dt and calls the ones that are due, oldest first. A timer added by a callback
    // waits for the next step; a repeating timer fires at most once a step.
    private void FireTimers(Instance instance, double dt)
    {
        var timers = instance.Timers;
        if (timers.Count == 0)
            return;
        var count = timers.Count;
        // A reload or stop inside a callback gives the instance a new list; the old timers end there.
        for (var i = 0; i < count && instance.Fault is null && ReferenceEquals(timers, instance.Timers); i++)
        {
            var timer = timers[i];
            if (timer.Cancelled)
                continue;
            timer.Remaining -= dt;
            if (timer.Remaining > 0)
                continue;
            if (timer.Interval > 0)
                timer.Remaining = Math.Max(timer.Remaining + timer.Interval, 0);
            else
                timer.Cancelled = true;
            Run(instance, timer.Function, "timer", DynValue.NewNumber(timer.Id));
        }
        timers.RemoveAll(t => t.Cancelled);
    }
}
