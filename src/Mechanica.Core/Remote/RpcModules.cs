using System.Text.Json;
using System.Text.RegularExpressions;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>What an endpoint connection may do. A connection holds a set of scopes.</summary>
public enum AgentScope
{
    /// <summary>Read-only queries, <c>mechanica.describe</c> and events.</summary>
    Observe,

    /// <summary>State-changing Action API calls, under the same validation as everyone else.</summary>
    Play,

    /// <summary>Debug actions (spawning, raids, giving items), session control and the mod tools.</summary>
    Admin,
}

/// <summary>The context of one endpoint method call.</summary>
/// <param name="Caller">Who the call acts as.</param>
/// <param name="Scopes">What the connection may do.</param>
/// <param name="ConnectionId">The connection's id, unique for the endpoint's lifetime.</param>
public sealed record RpcContext(ActionCaller Caller, IReadOnlySet<AgentScope> Scopes, string ConnectionId);

/// <summary>JSON-RPC error codes a module method may answer with (through <see cref="RpcException"/>).</summary>
public static class RpcErrors
{
    /// <summary>The host refused the request; the message says why.</summary>
    public const int Rejected = JsonRpcEndpoint.Codes.Rejected;

    /// <summary>The params don't fit the method.</summary>
    public const int BadParams = JsonRpcEndpoint.Codes.InvalidParams;

    /// <summary>The request can't be done right now (something else is running).</summary>
    public const int Busy = JsonRpcEndpoint.Codes.Unavailable;

    /// <summary>The connection lacks the scope the method needs.</summary>
    public const int Forbidden = JsonRpcEndpoint.Codes.Forbidden;
}

/// <summary>Thrown by a module method to answer with a JSON-RPC error.</summary>
/// <param name="code">One of <see cref="RpcErrors"/>.</param>
/// <param name="message">What went wrong, for the agent.</param>
/// <param name="data">Optional extra detail, serialized as the error's <c>data</c>.</param>
public sealed class RpcException(int code, string message, object? data = null) : Exception(message)
{
    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; } = code;

    /// <summary>Extra detail, written as the error's <c>data.detail</c>, or null.</summary>
    public object? Detail { get; } = data;
}

/// <summary>One endpoint method outside the Action API, made with <see cref="RpcMethod.Create"/>.</summary>
public sealed class RpcMethodInfo
{
    internal RpcMethodInfo(string name, AgentScope scope, string description, Type paramsType,
        Func<RpcContext, JsonElement, JsonElement?>? invoke, Func<RpcContext, JsonElement, Task<JsonElement?>>? invokeAsync = null)
    {
        Name = name;
        Scope = scope;
        Description = description;
        ParamsType = paramsType;
        Invoke = invoke;
        InvokeAsync = invokeAsync;
    }

    /// <summary>Lowercase dotted name, like <c>mods.validate</c>.</summary>
    public string Name { get; }

    /// <summary>The scope a connection needs to call it.</summary>
    public AgentScope Scope { get; }

    /// <summary>What it does, for <c>mechanica.describe</c>.</summary>
    public string Description { get; }

    /// <summary>The params record; <c>mechanica.describe</c> derives its JSON Schema from it.</summary>
    public Type ParamsType { get; }

    /// <summary>True when the handler finishes later (see <see cref="RpcMethod.CreateAsync"/>).</summary>
    public bool IsAsync => InvokeAsync is not null;

    /// <summary>Parses the params and runs a synchronous handler. Throws <see cref="RpcException"/> for errors.</summary>
    internal Func<RpcContext, JsonElement, JsonElement?>? Invoke { get; }

    /// <summary>Parses the params and starts an asynchronous handler.</summary>
    internal Func<RpcContext, JsonElement, Task<JsonElement?>>? InvokeAsync { get; }
}

/// <summary>Builds <see cref="RpcMethodInfo"/>s.</summary>
public static partial class RpcMethod
{
    /// <summary>
    /// Makes a method. <typeparamref name="TParams"/> is a snake_case record like an action's arguments
    /// (unknown members are rejected; use <see cref="NoArgs"/> for none). The handler returns the result,
    /// or null for a JSON null, and throws <see cref="RpcException"/> to answer with an error.
    /// </summary>
    public static RpcMethodInfo Create<TParams>(string name, AgentScope scope, string description,
        Func<RpcContext, TParams, JsonElement?> handler)
        where TParams : class
    {
        if (!ValidName().IsMatch(name ?? ""))
            throw new ArgumentException($"'{name}' is not a valid method name (lowercase dotted, like \"mods.validate\").", nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        return new RpcMethodInfo(name!, scope, description ?? "", typeof(TParams), (context, parameters) => handler(context, Parse<TParams>(parameters)));
    }

    /// <summary>
    /// Makes a method whose handler finishes later, such as one that waits for Godot frames. The endpoint
    /// answers when the task completes; the task must complete on the game's main thread. Params are checked
    /// before the handler starts. Inside a batch, a call that hasn't finished is answered on its own later.
    /// </summary>
    public static RpcMethodInfo CreateAsync<TParams>(string name, AgentScope scope, string description,
        Func<RpcContext, TParams, Task<JsonElement?>> handler)
        where TParams : class
    {
        if (!ValidName().IsMatch(name ?? ""))
            throw new ArgumentException($"'{name}' is not a valid method name (lowercase dotted, like \"mods.validate\").", nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        return new RpcMethodInfo(name!, scope, description ?? "", typeof(TParams), null,
            (context, parameters) => handler(context, Parse<TParams>(parameters)));
    }

    private static TParams Parse<TParams>(JsonElement parameters)
        where TParams : class
    {
        TParams? args;
        try
        {
            args = parameters.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.Deserialize<TParams>("{}", ActionJson.Options)
                : parameters.Deserialize<TParams>(ActionJson.Options);
        }
        catch (JsonException e)
        {
            throw new RpcException(RpcErrors.BadParams, e.Message);
        }
        return args ?? throw new RpcException(RpcErrors.BadParams, "params must be an object of named arguments.");
    }

    /// <summary>Serializes a result value with the Action API's JSON settings (snake_case).</summary>
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);

    [GeneratedRegex("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ValidName();
}

/// <summary>A group of endpoint methods. Register it with <see cref="JsonRpcEndpoint.AddModule"/>.</summary>
public interface IRpcModule
{
    /// <summary>The methods it adds.</summary>
    IEnumerable<RpcMethodInfo> Methods { get; }
}
