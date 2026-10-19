using Godot;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// A mod's language file (<c>lang/eo.json</c> in the test mod): it adds Esperanto to the languages, translates the mod's
/// own text where it shows whole (a verbatim label, a name passed into built text) and rewords the game's UI text.
/// Without the mod, everything is back as it was.
/// </summary>
public partial class ModLanguageTest : StagedTest
{
    private Label _name = null!;
    private Label _menu = null!;
    private Label _chat = null!;

    protected override string Tag => "modlang";

    public override void _Ready()
    {
        Add("the mod's language is listed and used", _ =>
        {
            Loc.UseMods([ProjectSettings.GlobalizePath("res://tests/modlang/mods/lang_test")]);
            Check(Loc.Languages().Any(l => l is { Code: "eo", Name: "Esperanto" }), $"Esperanto is listed ({string.Join(", ", Loc.Languages().Select(l => l.Code))})");
            Loc.Use("eo");
            Check(Loc.Current.Code == "eo", $"Esperanto is in use ({Loc.Current.Code})");
            _name = Loc.Verbatim(new Label { Text = "Lantern" });
            _menu = new Label { Text = "New game" };
            _chat = Loc.Verbatim(new Label { Text = "a lantern please" });
            AddChild(_name);
            AddChild(_menu);
            AddChild(_chat);
            return true;
        });
        Add("mod text and game text show translated", frame =>
        {
            if (frame < 2)
                return false;
            Check(_name.Tr(_name.Text) == "Lanterno", $"a mod name shown whole is translated ({_name.Tr(_name.Text)})");
            Check(_menu.Tr(_menu.Text) == "Nova ludo", $"the mod rewords game text ({_menu.Tr(_menu.Text)})");
            Check(_chat.Tr(_chat.Text) == "a lantern please", $"other verbatim text shows as written ({_chat.Tr(_chat.Text)})");
            Check(Loc.T("open the {0}", "Lantern") == "malfermu la Lanterno", $"built text takes the mod's translations ({Loc.T("open the {0}", "Lantern")})");
            Check(Loc.Mod("Lantern") == "Lanterno" && Loc.Mod("Seat") == "Seat", "Loc.Mod looks up the mods' strings");
            Check(!Loc.Translated(_name) && Loc.Translated(_menu), "verbatim labels stay off the game's tables");
            return true;
        });
        Add("without the mod everything is as written", frame =>
        {
            if (frame == 0)
            {
                Loc.UseMods([]);
                Loc.Use("en");
                return false;
            }
            Check(Loc.Languages().All(l => l.Code != "eo"), "Esperanto is gone");
            Check(_name.Tr(_name.Text) == "Lantern" && _menu.Tr(_menu.Text) == "New game", "the labels show English");
            Check(Loc.T("open the {0}", "Lantern") == "open the Lantern", "built text is English");
            return true;
        });
    }
}
