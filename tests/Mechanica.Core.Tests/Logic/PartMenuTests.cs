using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

/// <summary>Part menus (UI spec §12): sequences, the seat's creation hotbar, menu commands and part readouts.</summary>
public partial class LogicTests
{
    [Theory]
    [InlineData("[]", null)]
    [InlineData("[0, 45]", null)]
    [InlineData("[[{\"part\": 2, \"target\": 30, \"speed\": 1}], []]", null)]
    [InlineData("[0, 0, 0, 0]", "at most 3 frames")]
    [InlineData("[120]", "between -90 and 90")]
    [InlineData("[[{\"part\": 2, \"target\": 30}]]", "is {\"part\", \"target\", \"speed\"}")]
    [InlineData("[[{\"part\": 2, \"target\": 30, \"speed\": 1, \"x\": 1}]]", "is {\"part\", \"target\", \"speed\"}")]
    [InlineData("[[{\"part\": 2, \"target\": 30, \"speed\": 1}, {\"part\": 2, \"target\": 0, \"speed\": 1}]]", "lists part 2 twice")]
    [InlineData("[[{\"part\": 2, \"target\": 100, \"speed\": 1}]]", "each target of Frames must be between -90 and 90")]
    [InlineData("[[{\"part\": 2, \"target\": 0, \"speed\": 0}]]", "more than 0 and at most 5")]
    [InlineData("[[{\"part\": 2, \"target\": 0, \"speed\": 6}]]", "more than 0 and at most 5")]
    [InlineData("[\"a\"]", "a list of joints")]
    [InlineData("{}", "a list of frames")]
    public void SequenceValuesAreChecked(string json, string? error)
    {
        var result = PartSettings.Check(Steps, JsonDocument.Parse(json).RootElement);
        if (error is null)
            Assert.Null(result);
        else
            Assert.Contains(error, result);
    }

    [Fact]
    public void SequenceFramesReadBothFormsAndRoundTrip()
    {
        var old = PartSettings.Frames(JsonDocument.Parse("[10, -20]").RootElement, [4, 5], 2f);
        Assert.Equal([new SequenceStep(4, 10, 2), new SequenceStep(5, 10, 2)], old[0]);
        Assert.Equal([new SequenceStep(4, -20, 2), new SequenceStep(5, -20, 2)], old[1]);

        IReadOnlyList<IReadOnlyList<SequenceStep>> frames = [[new SequenceStep(3, 45, 1.5f)], []];
        var value = PartSettings.Sequence(frames);
        Assert.Null(PartSettings.Check(Steps, value));
        var back = PartSettings.Frames(value, [], 1f);
        Assert.Equal(frames[0], back[0]);
        Assert.Empty(back[1]);
        Assert.Equal(JsonValueKind.Array, PartSettings.DefaultOf(Steps).ValueKind);
        Assert.Equal(0, PartSettings.DefaultOf(Steps).GetArrayLength());
    }

    [Fact]
    public void ASeatsHotbarHoldsTheUsablePartsItIsLinkedTo()
    {
        var c = Row(Seat, Switch, Lamp, Switch);
        Assert.True(c.TryLink(1, 4, out _));
        Assert.True(c.TryLink(1, 3, out _));
        Assert.True(c.TryLink(1, 2, out _));
        // Lamps have no use, so only the switches go on the bar, in part order.
        Assert.Equal([2, 4], SeatHotbar.Parts(c, 1));
        Assert.False(SeatHotbar.Holds(_seats, Peer, c, 2));
        _seats.Sit(Peer, 1, 1);
        Assert.True(SeatHotbar.Holds(_seats, Peer, c, 2));
        Assert.False(SeatHotbar.Holds(_seats, Peer, c, 3));
    }

    [Fact]
    public void ASeatedPlayerUsesTheirBarFromAnywhereOnTheCreation()
    {
        var c = Row(Seat, Switch, Switch);
        Assert.True(c.TryLink(1, 2, out _));
        _world.Eyes[Peer] = new Vector3(0, 1, 40);
        Assert.Contains("you can reach", Dispatch(Player, LogicActions.ActivatePart, new LogicPartArgs(1, 2)).Message);
        _seats.Sit(Peer, 1, 1);
        Assert.True(Dispatch(Player, LogicActions.ActivatePart, new LogicPartArgs(1, 2)).Ok);
        // A switch that is not on the seat's bar still needs reach.
        Assert.Contains("you can reach", Dispatch(Player, LogicActions.ActivatePart, new LogicPartArgs(1, 3)).Message);
        Assert.Equal([(Peer, 2)], _host.Activated);
    }

    [Fact]
    public void CommandPartOnlySendsCommandsThePartOffers()
    {
        Row(Sequencer, Lamp);
        var frames = JsonSerializer.SerializeToElement(new { playing = true, frames = new[] { new[] { new { part = 2, target = 30, speed = 1 } } } });
        Assert.Contains("takes no command 'jump'", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "jump", frames)).Message);
        Assert.Contains("takes no command", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 2, "preview", frames)).Message);
        var bad = JsonSerializer.SerializeToElement(new { playing = true, frames = new[] { 200 } });
        Assert.Contains("between -90 and 90", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "preview", bad)).Message);
        var big = JsonSerializer.SerializeToElement(new { text = new string('x', LogicActions.MaxCommandBytes) });
        Assert.Contains("over", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "preview", big)).Message);
        _host.RefuseCommand = "Sequencer takes no commands.";
        Assert.Equal("Sequencer takes no commands.", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "preview", frames)).Message);
        _host.RefuseCommand = null;
        _world.Eyes[Peer] = new Vector3(0, 1, 40);
        Assert.Contains("you can reach", Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "preview", frames)).Message);
        _world.Eyes[Peer] = new Vector3(0, 1, 2);
        Assert.True(Dispatch(Player, LogicActions.CommandPart, new PartCommandArgs(1, 1, "preview", frames)).Ok);
        Assert.Equal([(Peer, 1, "preview", frames.GetRawText())], _host.Commands);
    }
}
