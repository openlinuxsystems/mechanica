using Godot;
using Mechanica.Core.Net;
using Mechanica.Game.Net;

namespace Mechanica.Game.Menus;

/// <summary>
/// The main menu's Join game screen: games found on the LAN (with their world, host, players and mode),
/// and a box for an address and port. A game running another version is listed but can't be picked.
/// </summary>
public partial class JoinScreen : PanelContainer
{
    private ItemList _list = null!;
    private LineEdit _address = null!;
    private Label _status = null!;
    private List<LanEntry> _shown = [];

    /// <summary>Raised with where to join.</summary>
    public event Action<NetEndpoint>? JoinRequested;

    /// <summary>Raised by Back.</summary>
    public event Action? CloseRequested;

    /// <summary>The LAN browser, for tests that listen on another port. Set before adding to the tree, or one is made.</summary>
    public LanBrowser? Browser { get; set; }

    /// <summary>Games listed now.</summary>
    public IReadOnlyList<LanEntry> Listed => _shown;

    /// <summary>The address box.</summary>
    public LineEdit Address => _address;

    /// <summary>Why Join did nothing, or empty.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        AddChild(column);
        column.AddChild(new Label { Text = "Join game", ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label { Text = "Games on your network" });
        _list = new ItemList { CustomMinimumSize = new Vector2(0, 200) };
        _list.ItemActivated += i => JoinListed((int)i);
        _list.ItemSelected += i => _address.Text = _shown[(int)i].Endpoint.ToString();
        column.AddChild(_list);
        var row = new HBoxContainer();
        column.AddChild(row);
        _address = new LineEdit { PlaceholderText = $"address:port (port {NetProtocol.DefaultPort} if left out)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _address.TextSubmitted += _ => JoinAddress();
        row.AddChild(_address);
        _status = new Label { Modulate = new Color(1f, 0.6f, 0.5f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(buttons);
        var back = new Button { Text = "Back" };
        back.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(back);
        var refresh = new Button { Text = "Refresh" };
        refresh.Pressed += () => Browser?.Refresh();
        buttons.AddChild(refresh);
        var join = new Button { Name = "JoinButton", Text = "Join" };
        join.Pressed += JoinAddress;
        buttons.AddChild(join);

        Browser ??= new LanBrowser { Name = "Browser" };
        Browser.Changed += Refresh;
        if (Browser.GetParent() is null)
            AddChild(Browser);
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Browser is not null)
            Browser.Changed -= Refresh;
    }

    /// <summary>Joins the listed game at <paramref name="index"/>.</summary>
    public void JoinListed(int index)
    {
        var entry = _shown[index];
        if (!entry.Session.Compatible)
            _status.Text = $"That game runs another version of {Core.MechanicaInfo.GameName} (protocol {entry.Session.Protocol}, this is {NetProtocol.Version}).";
        else if (!entry.Session.HasRoom)
            _status.Text = "That game is full.";
        else
            JoinRequested?.Invoke(entry.Endpoint);
    }

    /// <summary>Joins the address in the box.</summary>
    public void JoinAddress()
    {
        if (!NetEndpoint.TryParse(_address.Text, NetProtocol.DefaultPort, out var endpoint, out var error))
        {
            _status.Text = error;
            return;
        }
        _status.Text = "";
        JoinRequested?.Invoke(endpoint!.Value);
    }

    private void Refresh()
    {
        _shown = [.. Browser?.Entries ?? []];
        _list.Clear();
        foreach (var entry in _shown)
        {
            var s = entry.Session;
            var text = $"{s.World}  ·  {s.Host}  ·  {s.Players}/{s.MaxPlayers} players  ·  {s.Mode}  ·  {entry.Endpoint}";
            var index = _list.AddItem(s.Compatible ? text : $"{text}  (other version)");
            _list.SetItemTooltip(index, s.Mods.Count == 0 ? "No mods" : "Mods: " + string.Join(", ", s.Mods));
            if (!s.Compatible || !s.HasRoom)
                _list.SetItemCustomFgColor(index, new Color(0.6f, 0.6f, 0.6f));
        }
        if (_shown.Count == 0)
            _list.AddItem("Looking for games…", selectable: false);
    }
}
