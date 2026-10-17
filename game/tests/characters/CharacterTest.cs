using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Player characters, host + clients over localhost ENet with the vanilla mods. Both shipped characters load and
/// pose on the shared rig. The host and a client each have to choose once, every peer draws each player as what
/// they chose, a second pick is refused, and a client that leaves and comes back keeps theirs. A walking player's
/// feet make footsteps on another peer, a held tool shows in their hand, a swing plays there, and a player who
/// goes into deep water swims everywhere.
/// </summary>
public partial class CharacterTest : StagedTest
{
    private const string Female = "vanilla:scrapper_female";
    private const string Male = "vanilla:scrapper_male";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _a = null!;
    private Instance _b = null!;
    private readonly List<CharacterEvent> _events = [];
    private CharacterModel? _watched;
    private bool _swung;
    private string? _refused;

    protected override string Tag => "characters";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();

        Add("the host spawns and must choose", _ => _host.Main.LocalPlayer is not null && _host.Main.Characters.LocalNeedsChoice);
        Add("both characters load on the shared rig", _ =>
        {
            var book = _host.Main.Characters.Book;
            Check(book.All.Select(c => c.QualifiedId).SequenceEqual([Male, Female]), $"vanilla has both scrappers ({string.Join(", ", book.All.Select(c => c.QualifiedId))})");
            var assets = _host.Main.Characters.Assets;
            Check(CharacterRig.Clips.All(assets.Clips.ContainsKey), "every clip of the animation set loads");
            foreach (var character in book.All)
            {
                var model = assets.Instantiate(character);
                Check(model?.FindChildren("*", nameof(Skeleton3D), owned: false).OfType<Skeleton3D>().FirstOrDefault() is { } skeleton
                    && CharacterRig.Bones.All(b => skeleton.FindBone(b) >= 0), $"{character.Name} is skinned to every rig bone");
                model?.Free();
            }
            foreach (var prop in Enum.GetValues<CharacterProp>().Where(p => CharacterRig.PropNode(p) is not null))
            {
                var made = assets.Prop(prop);
                Check(made is not null, $"the {prop} prop loads");
                made?.Node.Free();
            }
            Check(_host.Main.LocalPlayer!.Character is CharacterModel { Character.QualifiedId: Male }, "a player who has not chosen shows as the default");
            return true;
        });
        Add("the host chooses the female scrapper", frame =>
        {
            if (frame == 0)
                _host.Main.Characters.Choose("scrapper_female");
            return _host.Main.Characters.Choices.ChosenBy(_host.Peer) == Female && !_host.Main.Characters.LocalNeedsChoice
                && Check(Shown(_host, _host) == Female, "the host's own body is the female scrapper");
        });

        Add("a client joins and must choose", frame =>
        {
            if (frame == 0)
                _a = _harness.StartClient("ClientA");
            return _a.Main.LocalPlayer is not null && _a.Main.Characters.LocalNeedsChoice && Shown(_a, _host) == Female;
        });
        Add("the client chooses the male scrapper", frame =>
        {
            if (frame == 0)
                _a.Main.Characters.Choose(Male);
            return Shown(_host, _a) == Male && Shown(_a, _a) == Male && !_a.Main.Characters.LocalNeedsChoice;
        });
        Add("a second pick in the same world is refused", frame =>
        {
            if (frame == 0)
                _a.Main.Characters.Choose(Female, result => _refused = result.Ok ? "accepted" : result.Message);
            if (_refused is null)
                return false;
            Check(_refused != "accepted", $"the host refuses a second pick ({_refused})");
            return Check(Shown(_host, _a) == Male, "the client is still the male scrapper");
        });

        Add("a walking player's feet make footsteps on another peer", frame =>
        {
            if (frame == 0)
                Watch(_a, _host);
            _host.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            var steps = _events.Where(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight).ToList();
            if (frame < 90)
                return false;
            _host.Main.LocalPlayer!.CurrentInput = PlayerInput.None;
            Check(steps.Count >= 2, $"the client heard {steps.Count} footsteps");
            return Check(steps.Zip(steps.Skip(1)).All(p => p.First != p.Second), "feet alternate");
        });

        Add("a held hammer shows in the hand and a swing plays on another peer", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            player.HeldProp = CharacterProp.Hammer;
            if (frame == 10)
                player.Gesture(CharacterGesture.HammerSwing);
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            _swung |= model.Animator.Gesture == CharacterRig.GestureClip(CharacterGesture.HammerSwing);
            if (!_swung || model.PropNode?.Name != "hammer")
                return false;
            Check(model.PropNode.GetParent() is BoneAttachment3D { BoneName: "hand_r" }, "the hammer rides the right hand");
            return true;
        });
        Add("putting the tool away empties the hand", _ =>
        {
            _host.Main.LocalPlayer!.HeldProp = CharacterProp.None;
            return ((CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!).PropNode is null;
        });

        Add("a player in deep water swims everywhere", frame =>
        {
            var player = _host.Main.LocalPlayer!;
            var generator = _host.Main.Streamer.Generator;
            if (frame == 0)
            {
                Watch(_a, _host);
                var spot = Deep(generator, player.GlobalPosition);
                if (!Check(spot is not null, "there is deep water near spawn"))
                    return false;
                player.GlobalPosition = new Vector3(spot!.Value.X, generator.SeaLevel - 1.6f, spot.Value.Y);
                player.Velocity = Vector3.Zero;
            }
            var model = (CharacterModel)_a.Main.PlayerSync.Players[_host.Peer].Character!;
            if (!player.Body.HasFlag(BodyFlags.Swimming) || model.Animator.Mode != CharacterMode.Swim || !_events.Contains(CharacterEvent.WaterEnter))
                return false;
            return Check(MathF.Abs(player.Velocity.Y) < 3f, $"the swimmer floats ({player.Velocity.Y:F2} m/s)");
        });

        Add("a client that leaves and comes back keeps its character", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_a);
                _a = null!;
            }
            if (frame == 30)
                _a = _harness.StartClient("ClientA");
            if (_a?.Main.LocalPlayer is null || _a.Main.PlayerSync.Players.Count < 2)
                return false;
            return Shown(_a, _a) == Male && Shown(_host, _a) == Male && Shown(_a, _host) == Female
                && Check(!_a.Main.Characters.LocalNeedsChoice, "the returning client is not asked again");
        });
        Add("a late client sees everyone as they chose and chooses itself", frame =>
        {
            if (frame == 0)
                _b = _harness.StartClient("ClientB");
            if (_b.Main.LocalPlayer is null || _b.Main.PlayerSync.Players.Count < 3 || !_b.Main.Characters.LocalNeedsChoice)
                return false;
            return Shown(_b, _host) == Female && Shown(_b, _a) == Male;
        });
    }

    // What player `who` is drawn as on `on`.
    private static string? Shown(Instance on, Instance who) =>
        on.Main.PlayerSync.Players.TryGetValue(who.Peer, out var player) && player.Character is CharacterModel model ? model.Character.QualifiedId : null;

    private void Watch(Instance on, Instance who)
    {
        if (_watched is not null)
            _watched.Event -= _events.Add;
        _events.Clear();
        _watched = (CharacterModel)on.Main.PlayerSync.Players[who.Peer].Character!;
        _watched.Event += _events.Add;
    }

    // The nearest spot within 400 m whose sea floor is well under the surface.
    private static Vector2? Deep(Core.World.WorldGenerator generator, Vector3 from)
    {
        for (var radius = 0f; radius <= 400f; radius += 8f)
        {
            for (var a = 0; a < 32; a++)
            {
                var x = from.X + radius * MathF.Cos(a * MathF.Tau / 32);
                var z = from.Z + radius * MathF.Sin(a * MathF.Tau / 32);
                if (generator.HeightAt(x, z) < generator.SeaLevel - 4f)
                    return new Vector2(x, z);
            }
        }
        return null;
    }
}
