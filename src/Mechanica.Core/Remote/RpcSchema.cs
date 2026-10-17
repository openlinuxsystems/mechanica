using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Remote;

/// <summary>
/// Builds the JSON Schemas and descriptions <c>mechanica.describe</c> returns, from the argument records
/// themselves (snake_case, as <see cref="ActionJson.Options"/> reads them) and the XML documentation the
/// build writes next to each assembly. Descriptions are left out when an assembly has no XML file.
/// </summary>
public static partial class RpcSchema
{
    private static readonly Dictionary<Assembly, XmlDocs> Docs = [];
    private static readonly Dictionary<Assembly, Dictionary<string, string>> ActionNames = [];
    private static readonly Lock Gate = new();
    private static readonly List<string> SearchDirectories = [];

    /// <summary>
    /// Adds a folder to look in for an assembly's XML documentation when it isn't next to the assembly
    /// (Godot loads assemblies without a file location).
    /// </summary>
    public static void AddDocumentationDirectory(string directory)
    {
        lock (Gate)
        {
            if (!SearchDirectories.Contains(directory))
                SearchDirectories.Add(directory);
            // Assemblies that found nothing earlier get another look.
            foreach (var (assembly, docs) in Docs.ToList())
                if (docs.IsEmpty)
                    Docs.Remove(assembly);
            ActionNames.Clear();
        }
    }

    /// <summary>The JSON Schema of <paramref name="type"/> as a params object.</summary>
    public static JsonNode SchemaOf(Type type)
    {
        var schema = Build(type, 0);
        if (type != typeof(NoArgs) && DocsOf(type.Assembly).Summary(TypeKey(type)) is { Length: > 0 } summary && schema is JsonObject root)
            SetDescription(root, summary);
        return schema;
    }

    private const int MaxDepth = 8;

    private static JsonNode Build(Type type, int depth)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return OrNull(Build(inner, depth));
        if (Override(type) is { } fixedSchema)
            return fixedSchema;
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return new JsonObject { ["type"] = "integer" };
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        if (type.IsEnum)
        {
            var names = new JsonArray();
            foreach (var name in Enum.GetNames(type))
                names.Add(JsonNamingPolicy.SnakeCaseLower.ConvertName(name));
            return new JsonObject { ["type"] = "string", ["enum"] = names };
        }
        if (type == typeof(JsonElement) || type == typeof(JsonNode) || type == typeof(JsonObject) || type == typeof(object) || depth >= MaxDepth)
            return new JsonObject();
        if (DictionaryValue(type) is { } valueType)
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Build(valueType, depth + 1) };
        if (ElementType(type) is { } elementType)
            return new JsonObject { ["type"] = "array", ["items"] = Build(elementType, depth + 1) };

        JsonTypeInfo info;
        try
        {
            info = ActionJson.Options.GetTypeInfo(type);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return new JsonObject();
        }
        if (info.Kind != JsonTypeInfoKind.Object)
            return new JsonObject();
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in info.Properties)
        {
            var node = Build(property.PropertyType, depth + 1);
            var parameter = property.AssociatedParameter;
            var nullable = parameter?.IsNullable ?? property.IsSetNullable;
            if (nullable && !property.PropertyType.IsValueType)
                node = OrNull(node);
            if (node is not JsonObject obj)
                node = obj = new JsonObject();
            if (Describe(property) is { Length: > 0 } text)
                SetDescription(obj, obj["description"] is { } shape ? $"{text} Format: {shape.GetValue<string>()}" : text);
            if (parameter is { HasDefaultValue: true, DefaultValue: { } value })
            {
                try
                {
                    obj["default"] = JsonSerializer.SerializeToNode(value, value.GetType(), ActionJson.Options);
                }
                catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
                {
                    // No default shown; the schema is still right.
                }
            }
            properties[property.Name] = node;
            if (property.IsRequired || parameter is { HasDefaultValue: false })
                required.Add(property.Name);
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
            schema["required"] = required;
        schema["additionalProperties"] = false;
        return schema;
    }

    private static void SetDescription(JsonObject schema, string text)
    {
        schema.Remove("description");
        schema.Insert(0, "description", text);
    }

    private static JsonObject OrNull(JsonNode node) =>
        new() { ["anyOf"] = new JsonArray(node, new JsonObject { ["type"] = "null" }) };

    private static Type? DictionaryValue(Type type)
    {
        foreach (var candidate in type.GetInterfaces().Prepend(type))
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() is var d
                && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)) && candidate.GetGenericArguments()[0] == typeof(string))
                return candidate.GetGenericArguments()[1];
        return null;
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        foreach (var candidate in type.GetInterfaces().Prepend(type))
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return candidate.GetGenericArguments()[0];
        return null;
    }

    /// <summary>
    /// A description of the action <paramref name="action"/> taking <paramref name="argsType"/>: the doc
    /// comment on its action-name constant, then the argument record's summary. Empty when undocumented.
    /// </summary>
    public static string DescribeAction(string action, Type argsType)
    {
        var parts = new List<string>();
        foreach (var assembly in new[] { argsType.Assembly, typeof(RpcSchema).Assembly }.Distinct())
        {
            if (NameDocs(assembly).TryGetValue(action, out var doc))
            {
                parts.Add(doc);
                break;
            }
        }
        if (argsType != typeof(NoArgs) && DocsOf(argsType.Assembly).Summary(TypeKey(argsType)) is { Length: > 0 } summary)
            parts.Add(summary);
        return string.Join(" ", parts);
    }

    /// <summary>The documentation summary of <paramref name="type"/>, or empty.</summary>
    public static string Summary(Type type) => DocsOf(type.Assembly).Summary(TypeKey(type)) ?? "";

    private static JsonObject? Override(Type type)
    {
        static JsonObject Triple(string itemType, string what) => new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = itemType },
            ["minItems"] = 3,
            ["maxItems"] = 3,
            ["description"] = what,
        };
        if (type == typeof(Vec3))
            return Triple("number", "[x, y, z]");
        if (type == typeof(GridCell))
            return Triple("integer", "Grid cell [x, y, z] (0.25 m cells).");
        if (type == typeof(GridSize))
            return Triple("integer", "Size in grid cells [x, y, z].");
        if (type == typeof(GridRotation))
            return new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = "^[+-][xyz][+-][xyz]$",
                ["description"] = "Grid orientation: the directions the part's local +x and +y point, like \"+x+y\".",
            };
        if (type == typeof(System.Numerics.Quaternion))
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "number" },
                ["minItems"] = 4,
                ["maxItems"] = 4,
                ["description"] = "Rotation quaternion [x, y, z, w].",
            };
        return null;
    }

    private static string? Describe(JsonPropertyInfo property)
    {
        var declaring = property.DeclaringType;
        var docs = DocsOf(declaring.Assembly);
        var member = property.AttributeProvider as MemberInfo;
        var name = member?.Name ?? property.Name;
        var owner = member?.DeclaringType ?? declaring;
        return docs.Summary($"P:{TypeName(owner)}.{name}")
            ?? docs.Summary($"F:{TypeName(owner)}.{name}")
            ?? docs.Param(TypeKey(owner), name);
    }

    private static string TypeKey(Type type) => "T:" + TypeName(type);

    private static string TypeName(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return (definition.FullName ?? definition.Name).Replace('+', '.');
    }

    private static XmlDocs DocsOf(Assembly assembly)
    {
        lock (Gate)
        {
            if (!Docs.TryGetValue(assembly, out var docs))
                Docs[assembly] = docs = XmlDocs.Load(assembly, SearchDirectories);
            return docs;
        }
    }

    private static Dictionary<string, string> NameDocs(Assembly assembly)
    {
        lock (Gate)
        {
            if (ActionNames.TryGetValue(assembly, out var map))
                return map;
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            ActionNames[assembly] = map;
            var docs = DocsOf(assembly);
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = [.. e.Types.OfType<Type>()];
            }
            foreach (var type in types)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!field.IsLiteral || field.FieldType != typeof(string) || field.GetRawConstantValue() is not string value
                        || map.ContainsKey(value) || docs.Summary($"F:{TypeName(type)}.{field.Name}") is not { Length: > 0 } text)
                        continue;
                    map[value] = CleanActionDoc(text);
                }
            }
            return map;
        }
    }

    // "Action name for placing." reads oddly as a description; keep what it says the action does.
    private static string CleanActionDoc(string text)
    {
        var cleaned = ActionNamePrefix().Replace(text, "");
        return cleaned.Length == 0 ? text : char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    [GeneratedRegex("^(The )?[Aa]ction( name)?( for| to)?[:,]?\\s*")]
    private static partial Regex ActionNamePrefix();

    /// <summary>An assembly's XML documentation, keyed by member id (<c>T:</c>, <c>P:</c>, <c>F:</c>).</summary>
    private sealed partial class XmlDocs(Dictionary<string, XElement> members)
    {
        public bool IsEmpty => members.Count == 0;

        public static XmlDocs Load(Assembly assembly, IEnumerable<string> searchDirectories)
        {
            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
            try
            {
                var fileName = assembly.GetName().Name + ".xml";
                var location = assembly.Location;
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(location))
                    candidates.Add(Path.ChangeExtension(location, ".xml"));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));
                candidates.AddRange(searchDirectories.Select(d => Path.Combine(d, fileName)));
                var file = candidates.FirstOrDefault(File.Exists);
                if (file is not null)
                {
                    foreach (var member in XDocument.Load(file).Descendants("member"))
                        if (member.Attribute("name")?.Value is { } name)
                            members[name] = member;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // No descriptions, but the schemas still work.
            }
            return new XmlDocs(members);
        }

        public string? Summary(string key) =>
            members.TryGetValue(key, out var member) && member.Element("summary") is { } summary ? Text(summary) : null;

        public string? Param(string typeKey, string name) =>
            members.TryGetValue(typeKey, out var member)
                && member.Elements("param").FirstOrDefault(p => p.Attribute("name")?.Value == name) is { } param
                ? Text(param)
                : null;

        private static string? Text(XElement element)
        {
            var parts = new List<string>();
            foreach (var node in element.Nodes())
            {
                parts.Add(node switch
                {
                    XText text => text.Value,
                    XElement { Name.LocalName: "see" or "seealso" } see => see.Value is { Length: > 0 } v ? v
                        : ShortName(see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? see.Attribute("href")?.Value ?? ""),
                    XElement { Name.LocalName: "paramref" or "typeparamref" } r => r.Attribute("name")?.Value ?? "",
                    XElement e => Text(e) ?? "",
                    _ => "",
                });
            }
            var joined = Spaces().Replace(string.Concat(parts), " ").Trim();
            return joined.Length == 0 ? null : joined;
        }

        private static string ShortName(string cref)
        {
            var name = cref.Contains(':') ? cref[(cref.IndexOf(':') + 1)..] : cref;
            var paren = name.IndexOf('(');
            if (paren >= 0)
                name = name[..paren];
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name[(dot + 1)..] : name;
        }

        [GeneratedRegex("\\s+")]
        private static partial Regex Spaces();
    }
}
