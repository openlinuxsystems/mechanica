using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Core.Perception;
using Mechanica.Core.Quests;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Game.Remote;

/// <summary>
/// The host's view of the world and content for the read-only perception and catalog queries
/// (<see cref="PerceptionActions"/>, <see cref="CatalogActions"/>). It reads <see cref="Main"/>'s nodes when asked,
/// so it follows world loads and mod reloads.
/// </summary>
public sealed class PerceptionWorld(Main main) : IPerceptionWorld, IGameCatalog
{
    /// <inheritdoc/>
    public double Now => main.Survival.Now;

    /// <inheritdoc/>
    public long Tick => main.PlayerSync.Tick;

    /// <inheritdoc/>
    public int TickRate => Engine.PhysicsTicksPerSecond;

    /// <inheritdoc/>
    public GameMode Mode => main.Survival.Mode;

    /// <inheritdoc/>
    public Difficulty Difficulty => main.Survival.Difficulty;

    /// <inheritdoc/>
    public WorldGenerator? Generator => main.Streamer.Generator;

    /// <inheritdoc/>
    public WorldState? State => main.Streamer.State;

    /// <inheritdoc/>
    public IReadOnlyList<PlayerView> Players => [.. main.PlayerSync.Players.Values.Where(GodotObject.IsInstanceValid).Select(View)];

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, ItemDrop> Drops => main.Survival.Actions?.Drops ?? new Dictionary<int, ItemDrop>();

    /// <inheritdoc/>
    public IReadOnlyCollection<string> RevealedPois => main.Quests.State.Revealed;

    /// <inheritdoc/>
    public int CreationCount => main.Creations.Count;

    /// <inheritdoc/>
    public Creation? FindCreation(int id) => main.Creations.Find(id);

    /// <inheritdoc/>
    public RayHit? Cast(System.Numerics.Vector3 from, System.Numerics.Vector3 direction, float range, int ignorePlayer) =>
        main.Survival.Cast(from, direction, range, ignorePlayer);

    /// <inheritdoc/>
    public ModLoadResult Mods => main.Mods!;

    /// <inheritdoc/>
    public RecipeBook Recipes => main.Recipes ?? RecipeBook.Empty;

    /// <inheritdoc/>
    public BotBook Bots => main.BotTypes ?? BotBook.Empty;

    /// <inheritdoc/>
    public QuestBook Quests => main.QuestBook ?? QuestBook.Empty;

    /// <inheritdoc/>
    public CharacterBook Characters => main.CharacterBook ?? CharacterBook.Empty;

    private PlayerView View(Player.PlayerController player)
    {
        var peer = player.PeerId;
        var survival = main.Survival;
        var vitals = survival.Vitals.GetValueOrDefault(peer);
        var seat = main.Logic.Seats.SeatOf(peer);
        var held = main.Scripts.Runtime.Held.TryGetValue(peer, out var item) ? item.Item : null;
        var flags = player.Body;
        return new PlayerView(
            peer, player.DisplayName, main.Characters.Choices.ShownAs(peer)?.QualifiedId,
            player.GlobalPosition.ToNumerics().ToVec3(),
            (survival.EyeOf(peer) ?? player.GlobalPosition.ToNumerics()).ToVec3(),
            (survival.LookOf(peer) ?? -System.Numerics.Vector3.UnitZ).ToVec3(),
            player.Velocity.ToNumerics().ToVec3(),
            flags.HasFlag(BodyFlags.OnFloor), flags.HasFlag(BodyFlags.Swimming), player.Crouched,
            seat is { } s ? new SeatView(s.Creation, s.Part) : null,
            player.ShownProp == CharacterProp.None ? null : player.ShownProp,
            held, main.Build.Tools?.LiftOf(peer), vitals?.Health, vitals?.MaxHealth, vitals?.Dead ?? player.Dead);
    }
}
