using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mechanica.Core.Remote;

/// <summary>
/// Renders <c>mechanica.describe</c>'s answer as the Markdown method reference AIs are fed
/// (<c>docs/ai/reference.md</c>): every method grouped by game area, with its kind, scope, description and
/// a table of its params, then the event topics and error codes. The output depends only on the input, so
/// a test can tell when the checked-in copy has drifted from the game.
/// </summary>
public static class AiReference
{
    private static readonly Dictionary<string, string> GroupTitles = new(StringComparer.Ordinal)
    {
        ["mechanica"] = "Endpoint built-ins (mechanica.*)",
        ["remote"] = "Agent players",
        ["general"] = "Debug and general",
        ["ui"] = "Map markers",
        ["scripting"] = "Using held items",
        ["building"] = "Building",
        ["creations"] = "Creations",
        ["motion"] = "Motion and physics primitives",
        ["perception"] = "Seeing the world and the catalog",
        ["survival"] = "Survival",
        ["items"] = "Items, inventories and crafting",
        ["logic"] = "Logic, seats and part menus",
        ["quests"] = "Quests",
        ["bots"] = "Bots and raids",
        ["fire"] = "Fire",
        ["chat"] = "Chat",
        ["characters"] = "Characters",
        ["player"] = "Player",
        ["sound"] = "Sound",
        ["world"] = "World",
        ["package"] = "Mod packages (package.*)",
        ["mods"] = "Mod tools (mods.*)",
        ["lua"] = "Lua tools (lua.*)",
        ["editor"] = "Editors (editor.*)",
        ["reports"] = "Notes, recommendations and bug reports (reports.*)",
    };

    /// <summary>Renders the reference from a <c>mechanica.describe</c> result.</summary>
    public static string Markdown(JsonElement describe)
    {
        var methods = describe.GetProperty("methods").EnumerateArray().ToList();
        var groups = methods
            .GroupBy(m => Text(m, "group") is { Length: > 0 } g ? g : "general")
            .OrderBy(g => GroupOrder(g.Key))
            .ThenBy(g => Title(g.Key), StringComparer.Ordinal)
            .ToList();

        var md = new StringBuilder();
        md.Append("# Mechanica AI method reference\n\n");
        md.Append("<!-- Generated from mechanica.describe by tools/ai/gen-docs.sh. Do not edit by hand. -->\n\n");
        md.Append(CultureInfo.InvariantCulture, $"Protocol version {describe.GetProperty("protocol_version").GetInt32()}. ");
        md.Append(CultureInfo.InvariantCulture, $"{methods.Count} methods: {methods.Count(m => Text(m, "kind") == "action")} actions (change the world), ");
        md.Append(CultureInfo.InvariantCulture, $"{methods.Count(m => Text(m, "kind") == "query")} queries (only read it) and ");
        md.Append(CultureInfo.InvariantCulture, $"{methods.Count(m => Text(m, "kind") == "method")} endpoint methods. ");
        md.Append("Every one is a JSON-RPC method of the same name whose `params` are the named arguments below (snake_case). ");
        md.Append("`describe.json` beside this file is the same data as JSON Schemas, ready to become an AI's tool list. ");
        md.Append("Scopes: `observe` reads, `play` acts as a player, `admin` is for debug powers ([ai-endpoint.md](../ai-endpoint.md#scopes)).\n\n");

        md.Append("## Contents\n\n");
        foreach (var group in groups)
            md.Append(CultureInfo.InvariantCulture, $"- [{Title(group.Key)}](#{Anchor(Title(group.Key))}) ({group.Count()})\n");
        md.Append("- [Event topics](#event-topics)\n- [Error codes](#error-codes)\n\n");

        foreach (var group in groups)
        {
            md.Append(CultureInfo.InvariantCulture, $"## {Title(group.Key)}\n\n");
            foreach (var method in group.OrderBy(m => Text(m, "name"), StringComparer.Ordinal))
                Method(md, method);
        }

        md.Append("## Event topics\n\n");
        md.Append("Subscribe with `mechanica.subscribe {\"topics\": [...]}`; each arrives as a `mechanica.event` notification with `topic`, `tick` and `data`.\n\n");
        md.Append("| Topic | What it carries |\n|-------|-----------------|\n");
        foreach (var topic in describe.GetProperty("topics").EnumerateObject())
            md.Append(CultureInfo.InvariantCulture, $"| `{topic.Name}` | {Cell(topic.Value.GetString())} |\n");
        md.Append("\n## Error codes\n\n| `error.code` | Meaning |\n|--------------|---------|\n");
        foreach (var error in describe.GetProperty("errors").EnumerateObject())
            md.Append(CultureInfo.InvariantCulture, $"| {error.Name} | {Cell(error.Value.GetString())} |\n");
        return md.ToString();
    }

    /// <summary>
    /// The <c>mechanica.describe</c> result as the checked-in <c>docs/ai/describe.json</c>: indented, keys in
    /// the order the endpoint wrote them, non-ASCII text unescaped, ending with a newline.
    /// </summary>
    public static string Json(JsonElement describe) =>
        JsonSerializer.Serialize(describe, DescribeOptions).Replace("\r\n", "\n") + "\n";

    private static readonly JsonSerializerOptions DescribeOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void Method(StringBuilder md, JsonElement method)
    {
        var name = Text(method, "name");
        md.Append(CultureInfo.InvariantCulture, $"### `{name}`\n\n");
        md.Append(CultureInfo.InvariantCulture, $"*{Text(method, "kind")}, {Text(method, "scope")} scope*");
        if (method.TryGetProperty("max_args_length", out var max))
            md.Append(CultureInfo.InvariantCulture, $" · params up to {max.GetInt32()} characters");
        md.Append("\n\n");
        if (Text(method, "description") is { Length: > 0 } description)
            md.Append(description.Trim()).Append("\n\n");
        var rows = new List<string>();
        if (method.TryGetProperty("params", out var schema))
            Rows(rows, schema, "", 0);
        if (rows.Count == 0)
        {
            md.Append("No params.\n\n");
            return;
        }
        md.Append("| Param | Type | Required | Default | Description |\n|-------|------|----------|---------|-------------|\n");
        foreach (var row in rows)
            md.Append(row).Append('\n');
        md.Append('\n');
    }

    private static void Rows(List<string> rows, JsonElement schema, string prefix, int depth)
    {
        if (!schema.TryGetProperty("properties", out var properties))
            return;
        var required = schema.TryGetProperty("required", out var list)
            ? list.EnumerateArray().Select(r => r.GetString()).ToHashSet()
            : [];
        foreach (var property in properties.EnumerateObject())
        {
            var node = property.Value;
            var inner = NonNull(node);
            var defaultText = node.TryGetProperty("default", out var value) ? $"`{Cell(value.GetRawText())}`" : "";
            rows.Add($"| `{prefix}{property.Name}` | {Cell(TypeOf(node))} | {(required.Contains(property.Name) ? "yes" : "")} | {defaultText} | {Cell(Text(node, "description") is { Length: > 0 } d ? d : Text(inner, "description"))} |");
            if (depth < 2)
            {
                var nested = inner.TryGetProperty("items", out var items) ? NonNull(items) : inner;
                var suffix = inner.TryGetProperty("items", out _) ? "[]." : ".";
                if (nested.TryGetProperty("properties", out _))
                    Rows(rows, nested, $"{prefix}{property.Name}{suffix}", depth + 1);
            }
        }
    }

    private static JsonElement NonNull(JsonElement node)
    {
        if (node.TryGetProperty("anyOf", out var options))
        {
            foreach (var option in options.EnumerateArray())
                if (Text(option, "type") != "null")
                    return option;
        }
        return node;
    }

    private static string TypeOf(JsonElement node)
    {
        if (node.TryGetProperty("anyOf", out var options))
        {
            var kinds = options.EnumerateArray().Where(o => Text(o, "type") != "null").Select(TypeOf).ToList();
            var nullable = options.EnumerateArray().Any(o => Text(o, "type") == "null");
            return string.Join(" or ", kinds) + (nullable ? " or null" : "");
        }
        if (node.TryGetProperty("enum", out var values))
            return "one of " + string.Join(", ", values.EnumerateArray().Select(v => $"\"{v.GetString()}\""));
        var type = node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "any";
        switch (type)
        {
            case "array" when node.TryGetProperty("items", out var items):
                var count = node.TryGetProperty("minItems", out var min) && node.TryGetProperty("maxItems", out var maxItems)
                    && min.GetInt32() == maxItems.GetInt32() ? min.GetInt32().ToString(CultureInfo.InvariantCulture) : "";
                return $"{TypeOf(items)}[{count}]";
            case "object" when node.TryGetProperty("additionalProperties", out var map) && map.ValueKind == JsonValueKind.Object:
                return $"map of {TypeOf(map)}";
            case "string" when node.TryGetProperty("pattern", out var pattern):
                return $"string matching {pattern.GetString()}";
            default:
                return type;
        }
    }

    private static int GroupOrder(string group) => group switch
    {
        "mechanica" => 0,
        "remote" => 1,
        "perception" => 2,
        "package" or "mods" or "lua" or "editor" or "reports" => 4,
        _ => 3,
    };

    private static string Title(string group) =>
        GroupTitles.TryGetValue(group, out var title) ? title : char.ToUpperInvariant(group[0]) + group[1..];

    private static string Anchor(string title) =>
        new string(title.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Replace(' ', '-');

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "";

    private static string Cell(string? text) =>
        (text ?? "").Replace("\r", "").Replace("\n", " ").Replace("|", "\\|").Trim();
}
