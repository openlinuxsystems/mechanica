using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;

namespace Mechanica.Core.ModTools;

/// <summary>Params of methods that take one open document.</summary>
/// <param name="Doc">The handle <c>editor.part.new</c>, <c>editor.part.open</c>, <c>editor.bot.new</c> or <c>editor.bot.open</c> returned.</param>
public sealed record EditorDocParams(string Doc);

/// <summary>Params of <c>editor.part.new</c> and <c>editor.bot.new</c>.</summary>
/// <param name="Mod">Mod the new document belongs to.</param>
/// <param name="Template">Template name, from <c>editor.templates</c>.</param>
/// <param name="Id">New id, lowercase snake_case.</param>
/// <param name="Name">Display name; defaults to the id.</param>
public sealed record EditorNewParams(string Mod, string Template, string Id, string? Name = null);

/// <summary>Params of methods that take a file of a mod.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Path">Forward-slash path inside the mod.</param>
public sealed record EditorFileParams(string Mod, string Path);

/// <summary>Params of methods that take a mod.</summary>
/// <param name="Mod">Mod id.</param>
public sealed record EditorModParams(string Mod);

/// <summary>Params of <c>editor.part.edit</c>.</summary>
/// <param name="Doc">Part document.</param>
/// <param name="Label">Undo label; defaults to "Edit".</param>
/// <param name="Definition">The whole part definition, replacing it.</param>
/// <param name="Set">Or: keys to merge into the definition (null removes a key, objects merge).</param>
/// <param name="Source">The model pieces, replacing them.</param>
public sealed record PartEditParams(string Doc, string? Label = null, JsonElement? Definition = null, JsonElement? Set = null, JsonElement? Source = null);

/// <summary>Params of <c>editor.part.sound</c>.</summary>
/// <param name="Doc">Part document.</param>
/// <param name="Key">The part's sound name, like <c>place</c> or <c>impact</c>.</param>
/// <param name="Sound">Sound event id, or null to unbind it.</param>
public sealed record PartSoundParams(string Doc, string Key, string? Sound = null);

/// <summary>Params of <c>editor.part.undo</c> and <c>editor.bot.undo</c>.</summary>
/// <param name="Doc">Document.</param>
/// <param name="Redo">True redoes instead.</param>
public sealed record EditorUndoParams(string Doc, bool Redo = false);

/// <summary>Params of <c>editor.bot.edit</c>.</summary>
/// <param name="Doc">Bot document.</param>
/// <param name="Set">Keys to merge into the bot (null removes a key).</param>
/// <param name="Label">Undo label; defaults to "Edit".</param>
public sealed record BotEditParams(string Doc, JsonElement Set, string? Label = null);

/// <summary>Params of <c>editor.bot.tree</c>.</summary>
/// <param name="Doc">Bot document.</param>
/// <param name="Op"><c>add</c>, <c>remove</c>, <c>move</c> or <c>set</c>.</param>
/// <param name="Path">Child indexes from the root of the behaviour tree; [] is the root.</param>
/// <param name="Type">add: the node type.</param>
/// <param name="Delta">move: places to move among its siblings (negative is earlier).</param>
/// <param name="Name">set: the parameter.</param>
/// <param name="Value">set: its value; null removes it.</param>
public sealed record BotTreeParams(string Doc, string Op, List<int> Path, string? Type = null, int? Delta = null, string? Name = null,
    JsonElement? Value = null);

/// <summary>Params of <c>editor.bot.script_save</c>.</summary>
/// <param name="Doc">Bot document.</param>
/// <param name="Code">The script.</param>
public sealed record BotScriptParams(string Doc, string Code);

/// <summary>Params of <c>editor.raids.save</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Raids">The raid table (the <c>raids/raids.json</c> schema).</param>
public sealed record RaidsSaveParams(string Mod, JsonElement Raids);

/// <summary>Params of <c>editor.recipes.save</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Recipe">The recipe (the <c>recipes/*.json</c> schema).</param>
/// <param name="PreviousFile">The file it was listed from, removed when its id changed.</param>
public sealed record RecipeSaveParams(string Mod, JsonElement Recipe, string? PreviousFile = null);

/// <summary>Params of <c>editor.sounds.get</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Path">Sound bank file under <c>sounds/</c>; null is the mod's default bank.</param>
public sealed record SoundBankParams(string Mod, string? Path = null);

/// <summary>Params of <c>editor.sounds.save</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Events">Every event of the bank, replacing them.</param>
/// <param name="Path">Sound bank file under <c>sounds/</c>; null is the mod's default bank.</param>
public sealed record SoundBankSaveParams(string Mod, JsonElement Events, string? Path = null);

/// <summary>A document after an edit, with what validation now says about it.</summary>
/// <param name="Document">The document.</param>
/// <param name="Diagnostics">Its problems, as export would report them.</param>
public sealed record EditResult<T>(T Document, IReadOnlyList<DiagnosticInfo> Diagnostics);

/// <summary>
/// The in-game editors as endpoint methods (AI support PR 7): Part Editor documents and batches, Bot Behavior Editor
/// documents and the raid table, recipes and sound banks. Every method needs the admin scope; every write answers
/// with diagnostics.
/// </summary>
public sealed class EditorModule(EditorService editor) : IRpcModule
{
    /// <inheritdoc />
    public IEnumerable<RpcMethodInfo> Methods =>
    [
        Method<NoArgs>("editor.templates",
            "Template names for editor.part.new and editor.bot.new.",
            _ => new { parts = editor.Host.PartTemplates, bots = editor.Host.BotTemplates }),

        Method<EditorNewParams>("editor.part.new",
            "Starts a Part Editor document from a template (block, wedge, bearing, …) in a mod. Nothing is written until editor.part.export. Returns the document (doc handle, definition, model pieces as source) and its diagnostics.",
            a => Part(editor.NewPart(a.Mod, a.Template, a.Id, a.Name))),
        Method<EditorFileParams>("editor.part.open",
            "Opens a part file of a mod (parts/… or overrides/…) with its model pieces. Returns the document and its diagnostics.",
            a => Part(editor.OpenPart(a.Mod, a.Path))),
        Method<EditorDocParams>("editor.part.get",
            "The part document as it stands: definition, source (model pieces), dirty, undo and redo labels, needs_bake.",
            a => editor.GetPart(a.Doc)),
        Method<PartEditParams>("editor.part.edit",
            "One undo step: replace the definition, or merge set into it (null removes a key, objects merge), and/or replace the model pieces (source). Returns the document and its diagnostics.",
            a => Part(editor.EditPart(a.Doc, a.Label, a.Definition, a.Set, a.Source))),
        Method<PartSoundParams>("editor.part.sound",
            "Binds one of the part's sound names to a sound event id, or unbinds it with a null sound. Returns the document and its diagnostics.",
            a => Part(editor.SetPartSound(a.Doc, a.Key, a.Sound))),
        Method<EditorUndoParams>("editor.part.undo",
            "Undoes one step (or redoes, with redo). Returns the document and its diagnostics.",
            a => Part(editor.UndoPart(a.Doc, a.Redo))),
        Method<EditorDocParams>("editor.part.validate",
            "Checks the part and its model pieces exactly as export would.",
            a => editor.ValidatePart(a.Doc)),
        Method<EditorDocParams>("editor.part.contacts",
            "The face contacts the part makes, as the socket preview shows them: cell, direction, connection, socket.",
            a => editor.PartContacts(a.Doc)),
        Async<EditorDocParams, ExportInfo>("editor.part.export",
            "Exports the part into its mod: the part file, its model pieces under editor/parts/, and a mesh baked by the editor's model builder when the pieces need one. Returns ok, id, file and diagnostics; mods.reload loads it.",
            a => editor.ExportPartAsync(a.Doc)),
        Async<EditorFileParams, BatchReport>("editor.batch.run",
            "Runs a batch file of a mod (Part Editor parts and recipes, Bot Behavior Editor bots and raids, or story POIs and quests) as tools/author-vanilla.sh does; this is how vanilla content is changed. Returns kind, ok, every export and the diagnostics.",
            a => editor.RunBatchAsync(a.Mod, a.Path)),

        Method<EditorNewParams>("editor.bot.new",
            "Starts a Bot Behavior Editor document from a template in a mod. Nothing is written until editor.bot.export. Returns the document (doc handle and the bot data, behaviour tree included) and its diagnostics.",
            a => Bot(editor.NewBot(a.Mod, a.Template, a.Id, a.Name))),
        Method<EditorFileParams>("editor.bot.open",
            "Opens a bot file of a mod (bots/…). Returns the document and its diagnostics.",
            a => Bot(editor.OpenBot(a.Mod, a.Path))),
        Method<EditorDocParams>("editor.bot.get",
            "The bot document as it stands: bot, dirty, undo label.",
            a => editor.GetBot(a.Doc)),
        Method<BotEditParams>("editor.bot.edit",
            "One undo step: merges set into the bot (stats, attacks, drops, script; null removes a key). Returns the document and its diagnostics.",
            a => Bot(editor.EditBot(a.Doc, a.Set, a.Label))),
        Method<BotTreeParams>("editor.bot.tree",
            "One behaviour tree edit at a node path (child indexes from the root): add a node of a type under it, remove it, move it by delta among its siblings, or set a parameter (name, value; null removes it). Returns the affected node's path, the document and its diagnostics.",
            a =>
            {
                var (path, state) = editor.EditBotTree(a.Doc, a.Op, a.Path, a.Type, a.Delta, a.Name, a.Value);
                return new { path, document = state, diagnostics = editor.ValidateBot(a.Doc) };
            }),
        Method<EditorUndoParams>("editor.bot.undo",
            "Undoes one step (or redoes, with redo). Returns the document and its diagnostics.",
            a => Bot(editor.UndoBot(a.Doc, a.Redo))),
        Method<EditorDocParams>("editor.bot.validate",
            "Checks the bot exactly as loading would, against the parts on disk now.",
            a => editor.ValidateBot(a.Doc)),
        Method<EditorDocParams>("editor.bot.export",
            "Exports the bot into its mod's bots/. Returns ok, id, file and diagnostics.",
            a => editor.ExportBot(a.Doc)),
        Method<EditorDocParams>("editor.bot.script",
            "The bot's own Lua script, or null when it has none in its mod.",
            a => editor.BotScript(a.Doc)),
        Method<BotScriptParams>("editor.bot.script_save",
            "Compiles the bot's own script, writes it into the mod's scripts/ and points the bot at it (export the bot to keep that). Refused when it doesn't compile. Returns the script path and the bot's diagnostics.",
            a => new { script = editor.SaveBotScript(a.Doc, a.Code), diagnostics = editor.ValidateBot(a.Doc) }),

        Method<EditorModParams>("editor.raids.get",
            "A mod's raid table (raids/raids.json), or null when it has none.",
            a => editor.Raids(a.Mod)),
        Method<RaidsSaveParams>("editor.raids.save",
            "Checks a raid table against the bots on disk and writes it to the mod's raids/raids.json. Returns ok, file and diagnostics.",
            a => editor.SaveRaids(a.Mod, a.Raids)),

        Method<EditorModParams>("editor.recipes.list",
            "Every recipe file of a mod, with its recipe.",
            a => editor.Recipes(a.Mod).Select(r => new { file = r.File, recipe = r.Recipe }).ToList()),
        Method<RecipeSaveParams>("editor.recipes.save",
            "Checks and writes a recipe as the Part Editor's Recipes tab does (previous_file is removed when the id changed). Returns ok, id, file and diagnostics.",
            a => editor.SaveRecipe(a.Mod, a.Recipe, a.PreviousFile)),
        Method<EditorFileParams>("editor.recipes.delete",
            "Deletes a recipe file of a mod.",
            a =>
            {
                editor.DeleteRecipe(a.Mod, a.Path);
                return null;
            }),

        Method<EditorModParams>("editor.sounds.list",
            "A mod's sound event files.",
            a => editor.SoundBanks(a.Mod)),
        Method<SoundBankParams>("editor.sounds.get",
            "The events of a sound bank (the mod's default bank when path is null); empty when it is new.",
            a => editor.SoundBank(a.Mod, a.Path)),
        Method<SoundBankSaveParams>("editor.sounds.save",
            "Replaces a sound bank's events, checks them as loading would and writes the file. Returns ok, id, file and diagnostics.",
            a => editor.SaveSoundBank(a.Mod, a.Path, a.Events)),

        Method<EditorDocParams>("editor.close",
            "Closes a part or bot document; unsaved changes are dropped. At most 64 documents are open at once.",
            a =>
            {
                editor.Close(a.Doc);
                return null;
            }),
    ];

    private EditResult<PartState> Part(PartState state) => new(state, editor.ValidatePart(state.Doc));

    private EditResult<BotState> Bot(BotState state) => new(state, editor.ValidateBot(state.Doc));

    private static RpcMethodInfo Method<TParams>(string name, string description, Func<TParams, object?> handler)
        where TParams : class =>
        RpcMethod.Create<TParams>(name, AgentScope.Admin, description, (_, a) =>
        {
            try
            {
                return handler(a) is { } value ? RpcMethod.Value(value) : null;
            }
            catch (ModToolException e)
            {
                throw new RpcException(RpcErrors.Rejected, e.Message);
            }
        });

    private static RpcMethodInfo Async<TParams, TResult>(string name, string description, Func<TParams, Task<TResult>> handler)
        where TParams : class =>
        RpcMethod.CreateAsync<TParams>(name, AgentScope.Admin, description, async (_, a) =>
        {
            try
            {
                return RpcMethod.Value(await handler(a));
            }
            catch (ModToolException e)
            {
                throw new RpcException(RpcErrors.Rejected, e.Message);
            }
        });
}
