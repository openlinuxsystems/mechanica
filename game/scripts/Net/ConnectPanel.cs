using Godot;
using Mechanica.Core.Net;

namespace Mechanica.Game.Net;

/// <summary>
/// Minimal host/join overlay, shown while the mouse is free (Esc). Menus proper come in chunk 24.
/// </summary>
public partial class ConnectPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private LineEdit _port = null!;
    private LineEdit _address = null!;
    private Button _host = null!;
    private Button _join = null!;
    private Button _leave = null!;
    private Label _status = null!;
    private string _hint = "";

    /// <summary>The session the buttons drive. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    public override void _Ready()
    {
        _panel = new PanelContainer { Position = new Vector2(16, 16) };
        AddChild(_panel);
        var rows = new VBoxContainer();
        _panel.AddChild(rows);

        var hostRow = new HBoxContainer();
        _port = new LineEdit { Text = NetProtocol.DefaultPort.ToString(), PlaceholderText = "Port", CustomMinimumSize = new Vector2(90, 0) };
        _host = new Button { Text = "Host" };
        _host.Pressed += OnHost;
        hostRow.AddChild(_port);
        hostRow.AddChild(_host);
        rows.AddChild(hostRow);

        var joinRow = new HBoxContainer();
        _address = new LineEdit { PlaceholderText = "address:port", CustomMinimumSize = new Vector2(200, 0) };
        _join = new Button { Text = "Join" };
        _join.Pressed += OnJoin;
        joinRow.AddChild(_address);
        joinRow.AddChild(_join);
        rows.AddChild(joinRow);

        _leave = new Button { Text = "Leave" };
        _leave.Pressed += () => Session.Leave();
        rows.AddChild(_leave);
        _status = new Label();
        rows.AddChild(_status);
    }

    public override void _Process(double delta)
    {
        _panel.Visible = Input.MouseMode != Input.MouseModeEnum.Captured;
        if (!_panel.Visible)
            return;
        var offline = Session.State == NetSession.SessionState.Offline;
        _host.Disabled = !offline;
        _join.Disabled = !offline;
        _leave.Disabled = offline;
        _status.Text = _hint.Length > 0 ? _hint : $"{Session.State}. {Session.LastMessage}";
    }

    private void OnHost()
    {
        if (!NetEndpoint.TryParsePort(_port.Text.Trim(), out var port))
        {
            _hint = "Enter a port from 1 to 65535.";
            return;
        }
        _hint = "";
        Session.Host(port);
    }

    private void OnJoin()
    {
        var ok = NetEndpoint.TryParse(_address.Text, NetProtocol.DefaultPort, out var endpoint, out var error);
        _hint = ok ? "" : error;
        if (ok)
            Session.Join(endpoint!.Value);
    }
}
