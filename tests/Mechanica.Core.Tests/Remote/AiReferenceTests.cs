using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Tests.Remote;

/// <summary>The Markdown method reference rendered from <c>mechanica.describe</c>.</summary>
public class AiReferenceTests
{
    private static JsonElement Describe(ActionDispatcher dispatcher)
    {
        var endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, new EndpointAccess(), null);
        var connection = endpoint.Connect();
        var text = endpoint.Handle(connection, """{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.describe" }""", 0);
        return JsonDocument.Parse(text!).RootElement.GetProperty("result").Clone();
    }

    [Fact]
    public void EveryMethodGetsAGroupedEntryWithAParamsTable()
    {
        var dispatcher = new ActionDispatcher();
        dispatcher.Register<PlacePartArgs>(BuildActions.PlacePart, (_, _) => null, (_, _) => null);
        dispatcher.Register<NoArgs>("Look", (_, _) => null, (_, _) => null, readOnly: true);
        new AgentPlayers(new AgentSettings()).Register(dispatcher);
        var describe = Describe(dispatcher);
        var md = AiReference.Markdown(describe);

        foreach (var method in describe.GetProperty("methods").EnumerateArray())
            Assert.Contains($"### `{method.GetProperty("name").GetString()}`", md);
        Assert.Contains("## Building", md);
        Assert.Contains("## Agent players", md);
        Assert.Contains("## Endpoint built-ins (mechanica.*)", md);
        Assert.True(md.IndexOf("## Endpoint built-ins", StringComparison.Ordinal) < md.IndexOf("## Building", StringComparison.Ordinal));
        Assert.Contains("| `cell` | integer[3] | yes |", md);
        Assert.Contains("| `ticks` | integer |  | `1` |", md);
        Assert.Contains("| `yaw` | number or null |", md);
        Assert.Contains("*action, play scope*", md);
        Assert.Contains("*query, observe scope*", md);
        Assert.Contains("| `hurt` |", md);
        Assert.Contains("| -32005 |", md);
    }

    [Fact]
    public void DescribeTagsActionsWithTheirArea()
    {
        var dispatcher = new ActionDispatcher();
        dispatcher.Register<PlacePartArgs>(BuildActions.PlacePart, (_, _) => null, (_, _) => null);
        new AgentPlayers(new AgentSettings()).Register(dispatcher);
        var groups = Describe(dispatcher).GetProperty("methods").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("group").GetString());
        Assert.Equal("building", groups[BuildActions.PlacePart]);
        Assert.Equal("remote", groups[AgentPlayers.SetPlayerInput]);
        Assert.Equal("mechanica", groups["mechanica.join"]);
    }

    [Fact]
    public void ComputedPropertiesAreNotOfferedAsParams()
    {
        var schema = RpcSchema.SchemaOf(typeof(Mechanica.Core.Items.GetInventoryArgs));
        var inventory = schema["properties"]!["inventory"]!["properties"]!.AsObject();
        Assert.True(inventory.ContainsKey("player"));
        Assert.False(inventory.ContainsKey("is_player"));
    }

    [Fact]
    public void OutputIsStableAndPipesAreEscaped()
    {
        var describe = JsonDocument.Parse("""
            {
              "protocol_version": 2,
              "methods": [ { "name": "A", "kind": "action", "group": "zeta", "scope": "play", "description": "Splits a | b.\nTwo lines.",
                             "params": { "type": "object", "properties": { "mode": { "type": "string", "enum": ["on", "off"] } }, "additionalProperties": false } } ],
              "topics": { "x": "about x" },
              "errors": { "-1": "bad" }
            }
            """).RootElement;
        var md = AiReference.Markdown(describe);
        Assert.Equal(md, AiReference.Markdown(describe));
        Assert.Contains("Splits a | b.", md);
        Assert.Contains("| `mode` | one of \"on\", \"off\" |", md);
        Assert.Contains("## Zeta", md);
        Assert.EndsWith("\n", AiReference.Json(describe));
        Assert.DoesNotContain("\r", AiReference.Json(describe));
    }
}
