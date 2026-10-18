using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

/// <summary>UI spec §9 and §10: hold Use to refine a resource node, right click to pick up, Force Build to place.</summary>
public class RefineTests
{
    private readonly SurvivalFixture _f = new(new SurvivalSettings
    {
        Reach = 5,
        PickupReach = 3,
        DropLifetime = 100,
        MaxDrops = 8,
        RefineSeconds = 1.2f,
        RefineCancelDistance = 1.5f,
    });

    private readonly WorldObjectId _tree = new(0, 1, 1);

    public RefineTests()
    {
        _f.World.Feet[Me.Peer] = Vector3.Zero;
        _f.Catalog.Add("test:wood", new PartDefinition { Id = "wood", Name = "Wood", Category = PartCategory.Resource, StackSize = 4,
            Sounds = new Dictionary<string, string> { ["pickup"] = "pickup_wood" } });
        _f.World.Objects[_tree] = new WorldObjectPlacement(_tree, new WorldObjectDefinition
        {
            Id = "tree", Health = 60, Refine = true, Drops = [new ObjectDrop { Item = "test:wood", Count = 6 }],
        }, new Vector3(0, 0, -2), 0, 1);
    }

    private object Tree => new { @object = _tree.ToString() };

    [Fact]
    public void HoldingUseLongEnoughRefinesTheNodeIntoTheInventory()
    {
        foreach (var s in Enumerable.Range(0, 3))
            _f.Pockets(Me).SetSlot(s, new ItemStack(Crop, 20));
        _f.Ok(Me, SurvivalActions.StartRefine, Tree);
        Assert.Equal(_tree.ToString(), _f.Survival.RefiningOf(Me.Peer)!.Value.Target);
        _f.World.Now += 0.5;
        Assert.Equal("Keep holding Use to refine it.", _f.Refused(Me, SurvivalActions.FinishRefine, Tree));
        _f.World.Now += 0.7;
        // Four fit in the stack; the other two stay where the tree stood.
        Assert.Equal(new RefineResult(4, 2), _f.Ok<RefineResult>(Me, SurvivalActions.FinishRefine, Tree));
        Assert.Equal(4, _f.Pockets(Me).Count("test:wood"));
        Assert.True(_f.World.State.IsDestroyed(_tree));
        Assert.Equal([new ItemStack("test:wood", 2)], Assert.Single(_f.Survival.Drops.Values).Items);
        // The Refine Tool's last stroke, then the wood going into the pockets.
        Assert.Equal([("refiner_complete", "vanilla"), ("pickup_wood", "test")], _f.World.Sounds.Select(s => (s.Sound, s.Mod)));
        Assert.Null(_f.Survival.RefiningOf(Me.Peer));
        Assert.Contains("nothing to refine", _f.Refused(Me, SurvivalActions.FinishRefine, Tree));
    }

    [Fact]
    public void ARefineNeedsAHoldThatWasNotCancelledOrWalkedAway()
    {
        Assert.Equal("Hold Use on it to refine it.", _f.Refused(Me, SurvivalActions.FinishRefine, Tree));
        _f.Ok(Me, SurvivalActions.StartRefine, Tree);
        _f.Ok(Me, SurvivalActions.CancelRefine, new { });
        _f.World.Now += 2;
        Assert.Equal("Hold Use on it to refine it.", _f.Refused(Me, SurvivalActions.FinishRefine, Tree));

        _f.Ok(Me, SurvivalActions.StartRefine, Tree);
        _f.World.Now += 2;
        _f.World.Feet[Me.Peer] = new Vector3(1.6f, 0, 0);
        Assert.Equal("You moved away, so the refine stopped.", _f.Refused(Me, SurvivalActions.FinishRefine, Tree));
        _f.World.Feet[Me.Peer] = new Vector3(1.4f, 0, 0);
        _f.Ok(Me, SurvivalActions.FinishRefine, Tree);
    }

    [Fact]
    public void EachPlayerRefinesOnTheirOwnAndOnlyRefinableNodesInReach()
    {
        var rock = new WorldObjectId(5, 0, 0);
        _f.World.Objects[rock] = new WorldObjectPlacement(rock, new WorldObjectDefinition { Id = "boulder" }, new Vector3(0, 0, -1), 0, 1);
        Assert.Equal("The boulder cannot be refined.", _f.Refused(Me, SurvivalActions.StartRefine, new { @object = rock.ToString() }));
        Assert.Contains("nothing to refine", _f.Refused(Me, SurvivalActions.StartRefine, new { @object = new WorldObjectId(9, 9, 9).ToString() }));

        _f.World.Feet[You.Peer] = new Vector3(0, 0, 0);
        _f.Ok(Me, SurvivalActions.StartRefine, Tree);
        _f.World.Now += 0.6;
        _f.Ok(You, SurvivalActions.StartRefine, Tree);
        _f.World.Now += 0.6;
        // Progress is per player: Me is done, You has only held for 0.6 s.
        Assert.Equal("Keep holding Use to refine it.", _f.Refused(You, SurvivalActions.FinishRefine, Tree));
        _f.Ok(Me, SurvivalActions.FinishRefine, Tree);
        _f.World.Now += 1;
        Assert.Contains("nothing to refine", _f.Refused(You, SurvivalActions.FinishRefine, Tree));

        _f.World.Objects[rock] = _f.World.Objects[rock] with { Definition = new WorldObjectDefinition { Id = "tree", Refine = true }, Position = new Vector3(0, 0, -20) };
        Assert.StartsWith("That is", _f.Refused(Me, SurvivalActions.StartRefine, new { @object = rock.ToString() }));
    }

    [Fact]
    public void PickingUpPlaysTheItemsPickupSound()
    {
        _f.Survival.AddDrop(new Vector3(0, 0, -1), [new ItemStack("test:wood", 2)]);
        _f.Ok(Me, SurvivalActions.PickUp, new { drop = _f.Survival.Drops.Keys.Single() });
        Assert.Equal(("pickup_wood", "test"), (_f.World.Sounds.Single().Sound, _f.World.Sounds.Single().Mod));
    }

    [Fact]
    public void ForceBuildPlacesItemsWhereAimedAndTheyStay()
    {
        _f.Pockets(Me).Add(Crop, 3);
        var slot = _f.SlotOf(Me, Crop);
        Assert.StartsWith("That is", _f.Refused(Me, SurvivalActions.DropItems, new { slot, count = 1, at = new Vec3(0, 1, -10) }));
        var placed = _f.Ok<DropItemsResult>(Me, SurvivalActions.DropItems, new { slot, count = 1, at = new Vec3(0.5f, 0.75f, -2) });
        var drop = _f.Survival.Drops[placed.Drop];
        Assert.True(drop.Placed);
        Assert.Equal(new Vec3(0.5f, 0.75f, -2), drop.Position);
        _f.World.Now += 1000;
        _f.Survival.Tick(0.1f);
        Assert.True(_f.Survival.Drops.ContainsKey(placed.Drop));
    }
}
