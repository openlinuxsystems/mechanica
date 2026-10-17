using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Scripting.Tests;

/// <summary>UI spec §10 weapons in mods/vanilla/scripts/potato_gun.lua: a magazine with Reload (R), the ammo counter and aiming.</summary>
public sealed class WeaponScriptTests : IDisposable
{
    private static readonly Vec3 Ahead = new(0, 0, -1);

    private readonly ItemRig _rig = new();
    private readonly List<(int Peer, ItemCounterView View)> _counters = [];
    private readonly string _gun;

    public WeaponScriptTests()
    {
        _gun = _rig.AddItem("rifle", "scripts/potato_gun.lua", new JsonObject
        {
            ["damage"] = 10, ["interval"] = 0.1, ["range"] = 30, ["ammo"] = "crop", ["magazine"] = 3, ["reload"] = 1,
            ["pellets"] = 3, ["spread"] = 8, ["aim_spread"] = 2,
        }, PartCategory.Weapon);
        _rig.Runtime.ItemCounterChanged += (peer, view) => _counters.Add((peer, view));
        F.World.Ray = (_, direction) => new RayHit(direction * 5);
    }

    private SurvivalFixture F => _rig.F;

    public void Dispose() => _rig.Dispose();

    private int Slot => F.SlotOf(Me, _gun);

    private void Wait(float seconds) => _rig.Runtime.Update(seconds);

    [Fact]
    public void AMagazineEmptiesThenReloadsFromTheAmmoCarried()
    {
        F.Pockets(Me).Add(_gun, 1);
        F.Pockets(Me).Add(Crop, 5);
        F.Ok(Me, ScriptRuntime.EquipItem, new { slot = Slot });
        // It comes loaded, and the holder is told what to show.
        Assert.Equal((Me.Peer, new ItemCounterView(_gun, 3, 3, Crop)), _counters[^1]);

        for (var shot = 0; shot < 3; shot++)
        {
            _rig.Used(Me, Slot, Ahead);
            Wait(0.2f);
        }
        Assert.Equal(new ItemCounterView(_gun, 0, 3, Crop), _counters[^1].View);
        Assert.Equal(2, F.Pockets(Me).Count(Crop));
        Assert.Equal("The Rifle is empty. Reload it.", _rig.Refused(Me, Slot, Ahead));

        F.Ok(Me, ScriptRuntime.ReloadItem, new { slot = Slot });
        Assert.Equal(new ItemCounterView(_gun, 2, 3, Crop, 1), _counters[^1].View);
        Assert.Equal("The Rifle is still reloading.", _rig.Refused(Me, Slot, Ahead));
        Assert.Equal("The Rifle is already reloading.", F.Refused(Me, ScriptRuntime.ReloadItem, new { slot = Slot }));
        Wait(1);
        Assert.Equal("The Rifle is already loaded.", F.Refused(Me, ScriptRuntime.ReloadItem, new { slot = Slot }));
        _rig.Used(Me, Slot, Ahead);
        Wait(0.2f);
        _rig.Used(Me, Slot, Ahead);
        Wait(0.2f);
        Assert.Equal("You have no Crop to fire.", _rig.Refused(Me, Slot, Ahead));
        Assert.Equal("You have no Crop to load.", F.Refused(Me, ScriptRuntime.ReloadItem, new { slot = Slot }));
    }

    [Fact]
    public void ItemsWithoutAReloadHookCannotBeReloaded()
    {
        F.Pockets(Me).Add(Blaster, 1); // no magazine
        F.Pockets(Me).Add(Crop, 4);
        Assert.Equal("The Blaster does not reload.", F.Refused(Me, ScriptRuntime.ReloadItem, new { slot = F.SlotOf(Me, Blaster) }));
        _rig.Used(Me, F.SlotOf(Me, Blaster), Ahead);
        // No magazine: the counter shows only the ammo carried.
        Assert.Equal(new ItemCounterView(Blaster, null, null, Crop), _counters[^1].View);
        F.Pockets(Me).Add(Scrap, 1);
        Assert.Equal("Scrap cannot be used.", F.Refused(Me, ScriptRuntime.ReloadItem, new { slot = F.SlotOf(Me, Scrap) }));
    }

    [Fact]
    public void AimingTightensTheSpread()
    {
        F.Pockets(Me).Add(_gun, 1);
        F.Pockets(Me).Add(Crop, 5);
        Assert.Equal("You have nothing in hand to aim.", F.Refused(Me, ScriptRuntime.AimItem, new { on = true }));
        _rig.Used(Me, Slot, Ahead);
        Assert.Equal(SurvivalActions.Pellets(-Vector3.UnitZ, 3, 8), F.World.Casts.Select(c => c.Direction), new Near(1e-5f));

        F.Ok(Me, ScriptRuntime.AimItem, new { on = true });
        Assert.True(_rig.Runtime.IsAiming(Me.Peer));
        F.World.Casts.Clear();
        Wait(0.2f);
        _rig.Used(Me, Slot, Ahead);
        Assert.Equal(SurvivalActions.Pellets(-Vector3.UnitZ, 3, 2), F.World.Casts.Select(c => c.Direction), new Near(1e-5f));

        // Putting the gun away (or dying) stops the aim.
        F.Ok(Me, ScriptRuntime.EquipItem, new { });
        Assert.False(_rig.Runtime.IsAiming(Me.Peer));
    }

    [Fact]
    public void CountersAndAimAreOnlyForItemHooks()
    {
        _rig.Write("scripts/probe.lua", """
            function can_use(target) item.counter({ value = 1 }) end
            """);
        var probe = _rig.AddItem("probe", "scripts/probe.lua");
        F.Pockets(Me).Add(probe, 1);
        Assert.Contains("item.counter changes the world", _rig.Refused(Me, F.SlotOf(Me, probe), Ahead));
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}
