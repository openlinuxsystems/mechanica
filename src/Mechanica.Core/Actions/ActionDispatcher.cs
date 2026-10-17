using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mechanica.Core.Actions;

/// <summary>
/// The host's single entry point for state changes (the unified Action API). Players, bots, scripts
/// and external agents all call <see cref="Dispatch"/> with an action name and JSON arguments; the
/// dispatcher parses the arguments strictly, runs the action's validation, and applies it only if
/// validation passes. Only the host owns a dispatcher; clients forward requests to it.
/// </summary>
public sealed partial class ActionDispatcher
{
    private readonly Dictionary<string, Func<ActionCaller, string, ActionResult>> _actions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readOnly = new(StringComparer.Ordinal);

    /// <summary>Raised after every dispatch, successful or not.</summary>
    public event Action<ActionRecord>? Dispatched;

    /// <summary>
    /// Checked before every action that changes state (queries skip it): null lets the call through, a
    /// reason rejects it. Survival uses it so a dead player can do nothing but respawn.
    /// </summary>
    public Func<ActionCaller, string, string?>? Gate { get; set; }

    /// <summary>Registered action names, sorted.</summary>
    public IReadOnlyList<string> Names => [.. _actions.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Registers an action. <paramref name="validate"/> returns null to accept or a reason to reject and
    /// must not change state; <paramref name="apply"/> runs only after it accepts and returns an optional
    /// result value. A <paramref name="readOnly"/> action only reads state (a query), so recordings skip it.
    /// </summary>
    /// <typeparam name="TArgs">Argument record, read from snake_case JSON. Unknown members are rejected.</typeparam>
    public void Register<TArgs>(
        string name,
        Func<ActionCaller, TArgs, string?> validate,
        Func<ActionCaller, TArgs, JsonElement?> apply,
        bool readOnly = false)
        where TArgs : class
    {
        if (!ValidName().IsMatch(name))
            throw new ArgumentException($"'{name}' is not a valid action name (PascalCase letters and digits).", nameof(name));
        if (!_actions.TryAdd(name, (caller, json) => Run(caller, json, validate, apply)))
            throw new ArgumentException($"Action '{name}' is already registered.", nameof(name));
        if (readOnly)
            _readOnly.Add(name);
    }

    /// <summary>True when <paramref name="name"/> is registered.</summary>
    public bool Has(string name) => _actions.ContainsKey(name);

    /// <summary>True when <paramref name="name"/> is a registered query that changes no state.</summary>
    public bool IsReadOnly(string name) => _readOnly.Contains(name);

    /// <summary>Validates and, if valid, applies one action. Never throws.</summary>
    public ActionResult Dispatch(ActionCaller caller, string name, string argsJson)
    {
        ActionResult result;
        if (!_actions.TryGetValue(name ?? "", out var run))
            result = ActionResult.Failure(ActionErrors.UnknownAction, $"No action named '{Shorten(name)}'.");
        else if (argsJson is null || argsJson.Length > ActionJson.MaxArgsLength)
            result = ActionResult.Failure(ActionErrors.BadArgs, $"Arguments must be a JSON object of at most {ActionJson.MaxArgsLength} characters.");
        else if (!_readOnly.Contains(name!) && Gate?.Invoke(caller, name!) is { } closed)
            result = ActionResult.Failure(ActionErrors.Rejected, closed);
        else
            result = run(caller, argsJson);
        Dispatched?.Invoke(new ActionRecord(caller, name ?? "", argsJson ?? "", result));
        return result;
    }

    /// <summary>Serializes <paramref name="args"/> and dispatches it (for in-process callers).</summary>
    public ActionResult Dispatch<TArgs>(ActionCaller caller, string name, TArgs args) =>
        Dispatch(caller, name, JsonSerializer.Serialize(args, ActionJson.Options));

    private static ActionResult Run<TArgs>(
        ActionCaller caller,
        string json,
        Func<ActionCaller, TArgs, string?> validate,
        Func<ActionCaller, TArgs, JsonElement?> apply)
        where TArgs : class
    {
        TArgs? args;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = ActionJson.Options.MaxDepth,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ActionResult.Failure(ActionErrors.BadArgs, "Arguments must be a JSON object.");
            args = document.RootElement.Deserialize<TArgs>(ActionJson.Options);
        }
        catch (JsonException e)
        {
            return ActionResult.Failure(ActionErrors.BadArgs, e.Message);
        }
        if (args is null)
            return ActionResult.Failure(ActionErrors.BadArgs, "Arguments must be a JSON object.");

        string? reason;
        try
        {
            reason = validate(caller, args);
        }
        catch (Exception e)
        {
            return ActionResult.Failure(ActionErrors.Internal, $"Validation failed: {e.GetType().Name}: {e.Message}");
        }
        if (reason is not null)
            return ActionResult.Failure(ActionErrors.Rejected, reason);

        try
        {
            return ActionResult.Success(apply(caller, args));
        }
        catch (Exception e)
        {
            return ActionResult.Failure(ActionErrors.Internal, $"{e.GetType().Name}: {e.Message}");
        }
    }

    private static string Shorten(string? text) => text is null ? "" : text.Length <= 64 ? text : text[..64] + "…";

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,63}$")]
    private static partial Regex ValidName();
}
