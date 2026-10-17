using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// A modal confirmation for destructive actions (UI spec §13 "Interaction Standards": delete a blueprint,
/// reset controls, discard changes). It dims the screen on its own canvas layer above everything, shows
/// a <see cref="ToolWindow"/> with the question and Cancel plus the confirm button, and frees itself
/// when answered. Esc or the X cancels.
/// </summary>
public partial class ConfirmDialog : CanvasLayer
{
    private Action? _onConfirm;
    private Action? _onCancel;

    /// <summary>The dialog's window.</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>The confirm button.</summary>
    public Button ConfirmButton { get; private set; } = null!;

    /// <summary>The question shown.</summary>
    public string Message { get; private set; } = "";

    /// <summary>The dialog open now, if any, for tests.</summary>
    public static ConfirmDialog? Current { get; private set; }

    /// <summary>
    /// Shows a confirmation under <paramref name="owner"/> (freed with it). <paramref name="destructive"/>
    /// draws the confirm button in the danger style instead of cyan.
    /// </summary>
    public static ConfirmDialog Ask(Node owner, string title, string message, string confirmText, Action onConfirm,
        Action? onCancel = null, bool destructive = true)
    {
        var dialog = new ConfirmDialog { Name = "ConfirmDialog", Layer = 100, _onConfirm = onConfirm, _onCancel = onCancel, Message = message };
        var dim = UiInput.KeepMouse(new ColorRect { Color = new Color(0, 0, 0, 0.5f) });
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dialog.AddChild(dim);
        // The window sits over the dim, which keeps every click and wheel off the game.
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dialog.AddChild(center);
        dialog.Window = new ToolWindow { Name = "Window", Title = title, CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(dialog.Window);
        dialog.Window.Body.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) });
        dialog.Window.CloseRequested += dialog.Cancel;
        dialog.Window.AddAction("Cancel", dialog.Cancel);
        dialog.ConfirmButton = dialog.Window.AddAction(confirmText, dialog.Confirm, primary: !destructive);
        if (destructive)
            dialog.ConfirmButton.ThemeTypeVariation = "DangerButton";
        owner.AddChild(dialog);
        Current = dialog;
        return dialog;
    }

    public override void _ExitTree()
    {
        if (Current == this)
            Current = null;
    }

    /// <summary>Answers yes.</summary>
    public void Confirm() => Answer(_onConfirm);

    /// <summary>Adds another answer next to the confirm button, such as Swap beside Overwrite.</summary>
    public Button AddChoice(string text, Action then) => Window.AddAction(text, () => Answer(then));

    /// <summary>Answers no.</summary>
    public void Cancel() => Answer(_onCancel);

    private void Answer(Action? then)
    {
        if (IsQueuedForDeletion())
            return;
        QueueFree();
        if (Current == this)
            Current = null;
        then?.Invoke();
    }
}
