using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Bots;

/// <summary>What the raid director needs from the host's world.</summary>
public interface IRaidWorld
{
    /// <summary>The host's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>The ground point under (or over) <paramref name="point"/>.</summary>
    Vector3 GroundBelow(Vector3 point);
}

/// <summary>
/// Raids (chunk 21, spec §16), run on the host. Every <see cref="RaidTable.CheckInterval"/> seconds,
/// once the last raid's cooldown is over and someone is playing, it sums the planted crop value; when that
/// reaches a level of the <see cref="RaidTable"/>, the raid horn sounds and after the warning the level's
/// bots arrive in a ring <see cref="RaidTable.SpawnDistance"/> from the farm (the planted crop with the most
/// crop value around it), scaled by the player count. Raiders go for that farm's crops. The raid ends
/// when every raider is dead, or after <see cref="RaidTable.Duration"/>, when the rest leave.
/// <para>
/// <c>StartRaid</c> sets one off now (players only in Creative, for testing), and <c>GetRaid</c> reads
/// the raid in progress. Raids happen in Survival only, unless started by hand.
/// </para>
/// </summary>
public sealed class RaidDirector(RaidTable table, BotActions bots, SurvivalActions survival, IRaidWorld world)
{
    /// <summary>Action name for starting a raid now.</summary>
    public const string StartRaid = "StartRaid";

    /// <summary>Action name for reading the raid in progress.</summary>
    public const string GetRaid = "GetRaid";

    /// <summary>How far round a planted crop other crops count toward its farm, in m.</summary>
    public const float FarmRadius = 16f;

    private readonly List<int> _raiders = [];
    private double _nextCheck = double.NaN;
    private double _cooldownUntil = double.NegativeInfinity;
    private int _nextId = 1;

    /// <summary>The raid table in use.</summary>
    public RaidTable Table => table;

    /// <summary>The raid in progress, or <see cref="RaidView.None"/>.</summary>
    public RaidView Current { get; private set; } = RaidView.None;

    /// <summary>Host time the current phase ends (warning) or the raid gives up (attacking).</summary>
    public double PhaseEnds { get; private set; }

    /// <summary>The raid's state changed: it started (warning), its raiders arrived, a raider fell, or it ended.</summary>
    public event Action<RaidView>? Changed;

    /// <summary>Registers <c>StartRaid</c> and <c>GetRaid</c>.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<StartRaidArgs>(StartRaid, ValidateStart, (_, a) =>
            JsonSerializer.SerializeToElement(Start(a.Level)!, ActionJson.Options));
        dispatcher.Register<NoArgs>(GetRaid, (_, _) => null, (_, _) => JsonSerializer.SerializeToElement(Current, ActionJson.Options), readOnly: true);
    }

    private string? ValidateStart(ActionCaller caller, StartRaidArgs args)
    {
        if (caller.Source == ActionSource.Player && survival.Mode != GameMode.Creative)
            return "Players can start raids only in Creative.";
        if (Current.Phase != RaidPhases.None)
            return "A raid is already on.";
        if (args.Level is { } level && table.Levels.All(l => l.Level != level))
            return $"There is no raid level {level}.";
        if (bots.Crops().Count == 0)
            return "Nothing is planted, so there is nothing to raid.";
        return args.Level is null && table.LevelFor(survival.CropValue()) is null
            ? $"The crops are worth {survival.CropValue():0.#}, less than the first raid level needs."
            : null;
    }

    /// <summary>
    /// Starts a raid now (the horn sounds; raiders arrive after the warning): at <paramref name="level"/>,
    /// or the level the crops set off. Null when nothing is planted or the level does not exist.
    /// </summary>
    public RaidView? Start(int? level = null)
    {
        var value = survival.CropValue();
        var players = Math.Max(1, survival.Players.Count());
        if (FarmTarget() is not { } target || table.Plan(value, players, level, survival.Rules.RaidBudget) is not { } plan)
            return null;
        Current = new RaidView(_nextId++, plan.Level, plan.Budget, value, players, RaidPhases.Warning, target.ToVec3(), plan.Bots.Count, plan.Bots);
        PhaseEnds = world.Now + table.Warning;
        Changed?.Invoke(Current);
        return Current;
    }

    /// <summary>
    /// Runs raids: checks the crops on schedule, brings the raiders when the warning ends, and ends a raid
    /// when its raiders are gone or its time is up. The host calls it every physics tick.
    /// </summary>
    public void Tick()
    {
        var now = world.Now;
        if (double.IsNaN(_nextCheck))
            _nextCheck = now + table.CheckInterval;
        switch (Current.Phase)
        {
            case RaidPhases.Warning when now >= PhaseEnds:
                Arrive();
                break;
            case RaidPhases.Attacking:
                var standing = _raiders.Count(id => bots.Find(id) is { Dead: false });
                if (standing == 0 || now >= PhaseEnds)
                    End();
                else if (standing != Current.Bots)
                {
                    Current = Current with { Bots = standing };
                    Changed?.Invoke(Current);
                }
                break;
            case RaidPhases.None when now >= _nextCheck:
                _nextCheck = now + table.CheckInterval;
                if (now >= _cooldownUntil && survival.Mode == GameMode.Survival && survival.Players.Any()
                    && table.LevelFor(survival.CropValue()) is not null)
                    Start();
                break;
        }
    }

    // The raiders arrive, evenly round a ring about the farm, and are told what to go for.
    private void Arrive()
    {
        var target = Current.Target.ToNumerics();
        var count = Current.Composition.Count;
        // A different start angle each raid, without randomness (replays agree).
        var turn = Current.Id * 2.3999632f;
        _raiders.Clear();
        for (var i = 0; i < count && bots.Bots.Count < bots.Settings.MaxBots; i++)
        {
            if (bots.Types.FindBot(Current.Composition[i]) is not { } type)
                continue;
            var angle = turn + 2 * MathF.PI * i / count;
            var ring = target + new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle)) * table.SpawnDistance;
            var feet = world.GroundBelow(ring + new Vector3(0, 50, 0)) + new Vector3(0, 0.05f, 0);
            var bot = bots.Spawn(type, feet, BotActions.YawOf(target - feet));
            bots.SetRaid(bot.Id, Current.Id);
            _raiders.Add(bot.Id);
        }
        Current = Current with { Phase = RaidPhases.Attacking, Bots = _raiders.Count };
        PhaseEnds = world.Now + table.Duration;
        Changed?.Invoke(Current);
    }

    private void End()
    {
        foreach (var id in _raiders)
        {
            if (bots.Find(id) is { Dead: false })
                bots.Remove(id);
        }
        _raiders.Clear();
        _cooldownUntil = world.Now + table.Cooldown * survival.Rules.RaidCooldown;
        Current = RaidView.None;
        Changed?.Invoke(Current);
    }

    /// <summary>The farm a raid goes for: the planted crop with the most crop value within <see cref="FarmRadius"/>; null when nothing is planted.</summary>
    public Vector3? FarmTarget()
    {
        var crops = bots.Crops();
        if (crops.Count == 0)
            return null;
        return crops
            .Select(c => (c.Position, Value: crops.Where(o => Vector3.Distance(o.Position, c.Position) <= FarmRadius).Sum(o => o.Value)))
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Position.X).ThenBy(c => c.Position.Z)
            .First().Position;
    }
}
