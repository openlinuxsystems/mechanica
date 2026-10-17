using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Quests;

/// <summary>A marker of a POI site, in the world.</summary>
/// <param name="Index">Index in the POI's markers.</param>
/// <param name="Type">Marker type.</param>
/// <param name="Ref">Marker ref, or null.</param>
/// <param name="Position">World position.</param>
public sealed record QuestMarker(int Index, string Type, string? Ref, Vector3 Position);

/// <summary>One placed POI.</summary>
/// <param name="Index">The world generator's POI index.</param>
/// <param name="Poi">Qualified POI id.</param>
/// <param name="Centre">Centre on its pad.</param>
/// <param name="Radius">Footprint radius.</param>
/// <param name="Markers">Its markers in the world.</param>
public sealed record QuestSite(int Index, string Poi, Vector3 Centre, float Radius, IReadOnlyList<QuestMarker> Markers)
{
    /// <summary>The site of a placed POI, with its markers turned and moved into the world.</summary>
    public static QuestSite Of(PoiInstance poi) => new(
        poi.Index,
        poi.Poi.QualifiedId,
        poi.ToWorld(Vector3.Zero),
        poi.Radius,
        [.. (poi.Poi.Prefab.Markers ?? []).Select((m, i) => new QuestMarker(i, m.Type ?? "", m.Ref, poi.ToWorld(m.At.ToNumerics())))]);
}

/// <summary>What the quest tracker reads and changes in the world. The game implements it on the host.</summary>
public interface IQuestWorld
{
    /// <summary>Players in the session.</summary>
    IEnumerable<int> Peers { get; }

    /// <summary>A player's feet, or null when they have no body.</summary>
    Vector3? FeetOf(int peer);

    /// <summary>Every placed site of a POI.</summary>
    IReadOnlyList<QuestSite> SitesOf(string poi);

    /// <summary>How many of an item a player holds.</summary>
    int CountItem(int peer, string item);

    /// <summary>Gives a player items; returns how many fit.</summary>
    int GiveItem(int peer, string item, int count);

    /// <summary>Crops planted in the world now.</summary>
    int PlantedCrops();

    /// <summary>Speed (m/s) of the Creation the player is seated in, or null when they are not seated.</summary>
    float? SeatedSpeed(int peer);
}

/// <summary>Arguments of <c>ChooseQuestOption</c>.</summary>
/// <param name="Quest">Quest id.</param>
/// <param name="Choice">Choice id of its current step.</param>
public sealed record ChooseQuestOptionArgs(string Quest, string Choice);

/// <summary>Value of a successful <c>QuestInteract</c>.</summary>
/// <param name="Quest">The quest the marker counted for.</param>
/// <param name="Marker">Marker type used.</param>
/// <param name="Ref">Its ref, or null.</param>
public sealed record QuestInteractResult(string Quest, string Marker, string? Ref);

/// <summary>Lines said to everyone, with the quest that said them.</summary>
/// <param name="Quest">Qualified quest id.</param>
/// <param name="Lines">The lines.</param>
public sealed record QuestDialogue(string Quest, IReadOnlyList<DialogueLine> Lines);

/// <summary>
/// The host's quest runtime (chunk 24). Quests start once their requirements are done and advance step by
/// step from what players do: Action API calls (<see cref="Record"/>), raids ending (<see cref="RaidEnded"/>),
/// what <see cref="Tick"/> sees (players near POIs, items held, crops planted, distance driven), and the
/// quest actions it registers: <c>QuestInteract</c> (use the nearest quest marker in reach),
/// <c>ChooseQuestOption</c> and <c>GetQuests</c>. Progress is shared by every player; finishing a quest gives
/// each player in the session its items, sets its flags and reveals its POIs.
/// </summary>
public sealed class QuestTracker(QuestBook book, IQuestWorld world, QuestState? state = null)
{
    /// <summary>Uses the nearest quest marker in reach.</summary>
    public const string QuestInteract = "QuestInteract";

    /// <summary>Picks a choice of a choose step.</summary>
    public const string ChooseQuestOption = "ChooseQuestOption";

    /// <summary>Reads the quest state.</summary>
    public const string GetQuests = "GetQuests";

    /// <summary>How far (m) from a marker a player can use it.</summary>
    public float Reach { get; init; } = 4f;

    /// <summary>The state now.</summary>
    public QuestState State { get; private set; } = state ?? new QuestState();

    /// <summary>The quests.</summary>
    public QuestBook Book => book;

    /// <summary>Raised after the state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when lines are said.</summary>
    public event Action<QuestDialogue>? Said;

    /// <summary>Replaces the state (a loaded save).</summary>
    public void Load(QuestState loaded)
    {
        State = loaded;
        Changed?.Invoke();
    }

    /// <summary>Registers the quest actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<NoArgs>(QuestInteract, (c, a) => FindUse(c.Peer, out _, out _) is { } error && TalkNear(c.Peer) is null ? error : null,
            (c, a) => Value(Interact(c.Peer) ?? Talk(c.Peer)!));
        dispatcher.Register<ChooseQuestOptionArgs>(ChooseQuestOption, (c, a) => CheckChoice(a.Quest, a.Choice), (c, a) => { Choose(a.Quest, a.Choice); return null; });
        dispatcher.Register<NoArgs>(GetQuests, (_, _) => null, (_, _) => State.ToElement(), readOnly: true);
        dispatcher.Dispatched += Record;
    }

    /// <summary>The current step of an active quest, or null.</summary>
    public QuestStepInfo? CurrentStep(string quest) =>
        book.Find(quest) is { } q && State.Of(q.QualifiedId) is { Status: QuestStatus.Active } p && p.Step < q.Steps.Count ? q.Steps[p.Step] : null;

    /// <summary>Starts ready quests and checks objectives the world shows. The host calls it a few times a second.</summary>
    public void Tick(float seconds)
    {
        StartReady();
        foreach (var (quest, step, progress) in Active().ToList())
        {
            switch (step.Kind)
            {
                case QuestObjectiveKind.Reach:
                    if (world.Peers.Any(p => world.FeetOf(p) is { } feet && world.SitesOf(step.Poi!).Any(s => Flat(feet, s.Centre) <= (step.Radius ?? s.Radius))))
                        Advance(quest);
                    break;
                case QuestObjectiveKind.HaveItem:
                    if (world.Peers.Any(p => world.CountItem(p, step.Target!) >= step.Count))
                        Advance(quest);
                    break;
                case QuestObjectiveKind.Crops:
                    var crops = world.PlantedCrops();
                    if (crops >= step.Count)
                        Advance(quest);
                    else if (crops != progress.Count)
                        Set(quest, progress with { Count = crops });
                    break;
                case QuestObjectiveKind.Drive:
                    var speed = world.Peers.Select(world.SeatedSpeed).Max() ?? 0f;
                    if (speed > 0.5f)
                    {
                        var driven = progress.Distance + speed * seconds;
                        if (driven >= step.Distance)
                            Advance(quest);
                        else if ((int)driven != (int)progress.Distance)
                            Set(quest, progress with { Distance = driven });
                        else
                            State.Quests[quest.QualifiedId] = progress with { Distance = driven }; // no event for fractions
                    }
                    break;
            }
        }
    }

    /// <summary>Counts a successful Action API call toward <c>place</c> and <c>craft</c> steps.</summary>
    public void Record(ActionRecord record)
    {
        if (!record.Result.Ok || record.Caller.Source != ActionSource.Player)
            return;
        var kind = record.Action switch
        {
            BuildActions.PlacePart => QuestObjectiveKind.Place,
            InventoryActions.Craft => QuestObjectiveKind.Craft,
            _ => (QuestObjectiveKind?)null,
        };
        if (kind is null)
            return;
        string? id;
        int count;
        try
        {
            using var args = JsonDocument.Parse(record.ArgsJson);
            var name = kind == QuestObjectiveKind.Place ? "part" : "recipe";
            id = args.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Identifiers.Qualify(value.GetString()!) : null;
            count = kind == QuestObjectiveKind.Craft && args.RootElement.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 1;
        }
        catch (JsonException)
        {
            return;
        }
        foreach (var (quest, step, progress) in Active().Where(a => a.Step.Kind == kind && (a.Step.Target is null || a.Step.Target == id)).ToList())
            Count(quest, step, progress, count);
    }

    /// <summary>Counts an ended raid toward <c>raid</c> steps.</summary>
    public void RaidEnded()
    {
        foreach (var (quest, step, progress) in Active().Where(a => a.Step.Kind == QuestObjectiveKind.Raid).ToList())
            Count(quest, step, progress, 1);
    }

    /// <summary>
    /// A player uses the nearest marker in reach that an active <c>interact</c> step still needs. Returns what
    /// counted, or null when nothing in reach does.
    /// </summary>
    public QuestInteractResult? Interact(int peer)
    {
        if (FindUse(peer, out var use, out var quest) is not null)
            return null;
        var (q, step, progress, key) = use!.Value;
        var used = new SortedSet<string>(progress.Used ?? [], StringComparer.Ordinal) { key };
        var marker = quest!;
        if (used.Count >= step.Count)
            Advance(q);
        else
            Set(q, progress with { Used = used, Count = used.Count });
        return new QuestInteractResult(q.QualifiedId, marker.Type, marker.Ref);
    }

    /// <summary>Null when <paramref name="choice"/> can be picked for <paramref name="quest"/> now, or why not.</summary>
    public string? CheckChoice(string quest, string choice)
    {
        if (CurrentStep(quest) is not { Kind: QuestObjectiveKind.Choose } step)
            return $"Quest '{quest}' is not waiting for a choice.";
        return step.Choices.Any(c => c.Id == choice) ? null : $"'{choice}' is not a choice of this step.";
    }

    /// <summary>Picks a choice: sets its flags, says its lines and finishes the step. False when it can't be picked.</summary>
    public bool Choose(string quest, string choice)
    {
        if (CheckChoice(quest, choice) is not null)
            return false;
        var q = book.Find(quest)!;
        var step = CurrentStep(quest)!;
        var picked = step.Choices.First(c => c.Id == choice);
        foreach (var (flag, value) in picked.Flags ?? [])
            State.Flags[flag] = value;
        var progress = State.Of(q.QualifiedId)!;
        var choices = new SortedDictionary<string, string>(progress.Choices ?? [], StringComparer.Ordinal) { [step.Id] = choice };
        State.Quests[q.QualifiedId] = progress with { Choices = choices };
        if (picked.Dialogue is { Count: > 0 } lines)
            Said?.Invoke(new QuestDialogue(q.QualifiedId, lines));
        Advance(q);
        return true;
    }

    /// <summary>
    /// Lines of the current step of an active quest when the player is in reach of an <c>npc</c> marker at the
    /// quest's POI (the step's, or the last one an earlier step named), for talking to it.
    /// </summary>
    public QuestDialogue? TalkNear(int peer)
    {
        if (world.FeetOf(peer) is not { } feet)
            return null;
        foreach (var (quest, step, progress) in Active())
        {
            // A step with no POI of its own (a choice) talks where the quest's last POI was.
            var poi = quest.Steps.Take(progress.Step + 1).LastOrDefault(s => s.Poi is not null)?.Poi;
            if (step.Dialogue.Count > 0 && poi is not null
                && world.SitesOf(poi).SelectMany(s => s.Markers).Any(m => m.Type == "npc" && Vector3.Distance(m.Position, feet) <= Reach))
                return new QuestDialogue(quest.QualifiedId, step.Dialogue);
        }
        return null;
    }

    /// <summary>
    /// What <c>QuestInteract</c> would do for <paramref name="peer"/> now, without doing it: the marker it would
    /// use, or the npc it would talk to (marker <c>npc</c>). Null when nothing is in reach. Clients call it on
    /// their copy of the state to show the prompt.
    /// </summary>
    public QuestInteractResult? Preview(int peer)
    {
        if (FindUse(peer, out var use, out var marker) is null)
            return new QuestInteractResult(use!.Value.Quest.QualifiedId, marker!.Type, marker.Ref);
        return TalkNear(peer) is { } talk ? new QuestInteractResult(talk.Quest, "npc", null) : null;
    }

    // Says the lines of the quest whose npc marker is in reach.
    private QuestInteractResult? Talk(int peer)
    {
        if (TalkNear(peer) is not { } talk)
            return null;
        Said?.Invoke(talk);
        return new QuestInteractResult(talk.Quest, "npc", null);
    }

    private string? FindUse(int peer, out (Quest Quest, QuestStepInfo Step, QuestProgress Progress, string Key)? use, out QuestMarker? marker)
    {
        use = null;
        marker = null;
        if (world.FeetOf(peer) is not { } feet)
            return "You have no body to reach with.";
        var best = float.MaxValue;
        foreach (var (quest, step, progress) in Active().Where(a => a.Step.Kind == QuestObjectiveKind.Interact))
        {
            foreach (var site in world.SitesOf(step.Poi!))
            {
                foreach (var m in site.Markers.Where(m => m.Type == step.Marker && (step.Ref is null || m.Ref == step.Ref)))
                {
                    var key = $"{site.Index}:{m.Index}";
                    var distance = Vector3.Distance(m.Position, feet);
                    if (distance <= Reach && distance < best && progress.Used?.Contains(key) != true)
                    {
                        best = distance;
                        use = (quest, step, progress, key);
                        marker = m;
                    }
                }
            }
        }
        return use is null ? "Nothing here for a quest." : null;
    }

    private IEnumerable<(Quest Quest, QuestStepInfo Step, QuestProgress Progress)> Active()
    {
        foreach (var (id, progress) in State.Quests)
        {
            if (progress.Status == QuestStatus.Active && book.Find(id) is { } quest && progress.Step < quest.Steps.Count)
                yield return (quest, quest.Steps[progress.Step], progress);
        }
    }

    private void StartReady()
    {
        foreach (var quest in book.All)
        {
            if (State.Of(quest.QualifiedId) is null && quest.Requires.All(State.IsDone))
            {
                Set(quest, new QuestProgress { Status = QuestStatus.Active });
                Say(quest, quest.Steps[0].Dialogue);
            }
        }
    }

    private void Count(Quest quest, QuestStepInfo step, QuestProgress progress, int amount)
    {
        if (progress.Count + amount >= step.Count)
            Advance(quest);
        else
            Set(quest, progress with { Count = progress.Count + amount });
    }

    // Finishes the current step: the next one begins, or the quest is done and pays out.
    private void Advance(Quest quest)
    {
        var progress = State.Of(quest.QualifiedId)!;
        var next = progress.Step + 1;
        if (next < quest.Steps.Count)
        {
            Set(quest, progress with { Step = next, Count = 0, Distance = 0, Used = null });
            Say(quest, quest.Steps[next].Dialogue);
            return;
        }
        State.Quests[quest.QualifiedId] = progress with { Status = QuestStatus.Done, Step = quest.Steps.Count, Count = 0, Distance = 0, Used = null };
        foreach (var peer in world.Peers.ToList())
        {
            foreach (var item in quest.Items)
                world.GiveItem(peer, item.Item, item.Count);
        }
        foreach (var (flag, value) in quest.Flags)
            State.Flags[flag] = value;
        foreach (var poi in quest.Reveal)
            State.Revealed.Add(poi);
        Say(quest, quest.Outro);
        Changed?.Invoke();
        StartReady();
    }

    private void Set(Quest quest, QuestProgress progress)
    {
        State.Quests[quest.QualifiedId] = progress;
        Changed?.Invoke();
    }

    private void Say(Quest quest, IReadOnlyList<DialogueLine> lines)
    {
        if (lines.Count > 0)
            Said?.Invoke(new QuestDialogue(quest.QualifiedId, lines));
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
