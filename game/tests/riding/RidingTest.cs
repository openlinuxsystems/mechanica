using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Riding a Creation as a client: a host and two clients over localhost ENet. A flat deck moves on the host, first
/// straight and then turning, with client A's player standing on it. Clients show the deck a few ticks behind the
/// host, so A's prediction is checked in the deck's frame: A must ride along smoothly (no corrections, an even step
/// every tick, moving at the deck's speed and not twice it), and client B must show A standing on the deck where
/// B shows it. A host teleport across the moving deck reaches A and sticks.
/// </summary>
public partial class RidingTest : StagedTest
{
    private const string Mod = "runtime_test";
    private const int Deck = 1;
    private const float Speed = 6f;
    private const float Turn = 0.5f;
    private static readonly Vector3 Start = new(0, 3, 10);

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _a = null!;
    private Instance _b = null!;
    private ModLoadResult _content = null!;
    private int _corrections;
    private Vector3 _last;
    private readonly List<float> _steps = [];
    private float _seenOff;

    protected override string Tag => "riding";

    public override void _Ready()
    {
        _content = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/creation/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _content);
        _a = _harness.StartClient("ClientA", mods: _content);
        _b = _harness.StartClient("ClientB", mods: _content);
        Add("host and two clients join", _ =>
            _host.Main.PlayerSync.Players.Count == 3 && _a.Main.LocalPlayer is not null && _b.Main.LocalPlayer is not null);
        Wait("players settle", 20);
        Add("spawn a floating deck", _ =>
        {
            var deck = new Creation(Deck, _content);
            for (var x = -8; x < 8; x++)
            {
                for (var z = -8; z < 8; z++)
                {
                    if (!deck.TryPlacePart($"{Mod}:block", new GridCell(x, 0, z), GridRotation.Identity, out PartInstance? _, out var error))
                        Check(false, $"place a block: {error}");
                }
            }
            deck.SetAllBodyPoses(new Transform3D(Basis.Identity, Start).ToPose());
            var node = _host.Main.Creations.Add(deck, _host.Main.Assets);
            foreach (var body in node.Bodies)
                body.GravityScale = 0;
            return true;
        });
        Add("A and B see the deck", _ => _a.Main.Creations.NodeOf(Deck) is not null && _b.Main.Creations.NodeOf(Deck) is not null);
        Add("A steps onto the deck", _ =>
        {
            _host.Main.PlayerSync.HostTeleport(_a.Peer, Start + new Vector3(0, 0.3f, 0));
            return true;
        });
        Add("A stands on it", frame => frame > 10 && _a.Main.LocalPlayer!.Ground?.Creation == Deck
            && _host.Main.PlayerSync.Players[_a.Peer].Ground?.Creation == Deck);
        Wait("A settles", 20);
        Add("the deck moves off", frame =>
        {
            Drive(Vector3.Zero);
            return frame >= 30;
        });
        Add("A rides it straight", frame =>
        {
            Drive(Vector3.Zero);
            if (frame == 0)
                Begin();
            Measure(frame);
            return frame >= 120;
        });
        Add("A rides along smoothly", _ =>
        {
            var mean = _steps.Average();
            var spread = _steps.Max() - _steps.Min();
            Check(Math.Abs(mean - Speed / Engine.PhysicsTicksPerSecond) < 0.01f,
                $"A moves at the deck's speed ({mean * Engine.PhysicsTicksPerSecond:F2} m/s, deck {Speed} m/s)");
            Check(spread < 0.01f, $"A moves the same each tick (steps vary by {spread * 1000:F1} mm)");
            Check(_a.Main.PlayerSync.Corrections - _corrections <= 1, $"A's prediction held ({_a.Main.PlayerSync.Corrections - _corrections} corrections)");
            Check(_seenOff < 0.03f, $"B shows A on the deck where the host has them (off by at most {_seenOff:F3} m)");
            return true;
        });
        Add("the deck turns", frame =>
        {
            Drive(new Vector3(0, Turn, 0));
            // Measured once the turn is under way.
            if (frame == 30)
                Begin();
            if (frame >= 30)
                Measure(frame - 30);
            return frame >= 180;
        });
        Add("A rides the turn", _ =>
        {
            Check(_a.Main.PlayerSync.Corrections - _corrections <= 2, $"A's prediction held through the turn ({_a.Main.PlayerSync.Corrections - _corrections} corrections)");
            Check(_a.Main.LocalPlayer!.Ground?.Creation == Deck, "A is still on the deck");
            Check(_seenOff < 0.03f, $"B shows A on the deck through the turn (off by at most {_seenOff:F3} m)");
            return true;
        });
        Add("the host moves A across the moving deck", frame =>
        {
            Drive(new Vector3(0, Turn, 0));
            if (frame == 0)
            {
                var player = _host.Main.PlayerSync.Players[_a.Peer];
                var deck = _host.Main.Creations.NodeOf(Deck)!.Bodies[0];
                _host.Main.PlayerSync.HostTeleport(_a.Peer, player.GlobalPosition + deck.GlobalBasis.X * 1f + Vector3.Up * 0.05f);
            }
            return frame >= 60;
        });
        Add("A's correction sticks on the deck", _ =>
        {
            // A correction on a Creation is kept in its frame: the next step carries A on from there, not back.
            var host = _host.Main.PlayerSync.Players[_a.Peer].Ground;
            var mine = _a.Main.LocalPlayer!.Ground;
            Check(host?.Creation == Deck && mine?.Creation == Deck, "A stands on the deck on the host and on A");
            if (host is { } h && mine is { } m)
                Check(System.Numerics.Vector3.Distance(h.Feet, m.Feet) < 0.1f, $"A stands where the host has them on the deck ({m.Feet} vs {h.Feet})");
            return true;
        });
    }

    // Keeps the deck moving on the host at Speed along its own -Z, turning at angular (rad/s).
    private void Drive(Vector3 angular)
    {
        foreach (var body in _host.Main.Creations.NodeOf(Deck)!.Bodies)
        {
            body.LinearVelocity = body.GlobalBasis * new Vector3(0, 0, -Speed);
            body.AngularVelocity = angular;
        }
    }

    private void Begin()
    {
        _corrections = _a.Main.PlayerSync.Corrections;
        _last = _a.Main.LocalPlayer!.GlobalPosition;
        _steps.Clear();
        _seenOff = 0;
    }

    private void Measure(int frame)
    {
        var position = _a.Main.LocalPlayer!.GlobalPosition;
        if (frame > 0)
            _steps.Add(position.DistanceTo(_last));
        _last = position;
        // Where B shows A against where B shows the deck, next to the host's own: the same in the deck's frame.
        if (frame < 10 || _host.Main.PlayerSync.Players[_a.Peer].Ground is not { } ground)
            return;
        var seen = _b.Main.PlayerSync.Players[_a.Peer].GlobalPosition;
        var deck = _b.Main.Creations.NodeOf(Deck)!.Bodies[0].GlobalTransform;
        _seenOff = Math.Max(_seenOff, (deck.AffineInverse() * seen).DistanceTo(ground.Feet.ToGodot()));
    }
}
