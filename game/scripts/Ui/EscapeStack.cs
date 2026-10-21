using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// Everything Esc can close, newest on top (UI spec §1: Esc closes the open window, or opens the menu when none is).
/// Every <see cref="ToolWindow"/> is on it by itself; any other panel the player opens over the world (the inventory,
/// the Paint Tool's ring, chat, the in-game menu itself) calls <see cref="Add"/> once and <see cref="Raise"/> each
/// time it opens. Esc closes <see cref="Topmost"/>; only when nothing is open does it open the in-game menu.
/// </summary>
public static class EscapeStack
{
    private sealed class Layer(Node owner, Func<bool> isOpen, Action close)
    {
        public Node Owner { get; } = owner;
        public Func<bool> IsOpen { get; } = isOpen;
        public Action Close { get; } = close;
        public long Order { get; set; }
    }

    private static readonly List<Layer> Layers = [];
    private static long _raised;

    /// <summary>
    /// Puts <paramref name="owner"/> on the stack, on top: <paramref name="isOpen"/> says whether it is showing and
    /// <paramref name="close"/> closes it. A panel out of the tree is never on top; a freed one drops off.
    /// </summary>
    public static void Add(Node owner, Func<bool> isOpen, Action close)
    {
        Layers.RemoveAll(l => l.Owner == owner || !GodotObject.IsInstanceValid(l.Owner));
        Layers.Add(new Layer(owner, isOpen, close) { Order = ++_raised });
    }

    /// <summary>Takes <paramref name="owner"/> off the stack.</summary>
    public static void Remove(Node owner) => Layers.RemoveAll(l => l.Owner == owner);

    /// <summary>Moves <paramref name="owner"/> to the top; call it when the panel opens.</summary>
    public static void Raise(Node owner)
    {
        if (Layers.Find(l => l.Owner == owner) is { } layer)
            layer.Order = ++_raised;
    }

    /// <summary>The open panel opened last, which Esc closes, or null when nothing is open.</summary>
    public static Node? Topmost => Top(_ => true);

    /// <summary>The topmost open panel that is a <typeparamref name="T"/>.</summary>
    public static T? TopmostOf<T>() where T : Node => Top(n => n is T) as T;

    /// <summary>
    /// True while a panel in <paramref name="viewport"/> is open, so a click on the screen around it must not take the
    /// mouse back for the game (each Sandbox instance is its own viewport, under the Sandbox's own window).
    /// </summary>
    public static bool AnyOpenIn(Viewport viewport) => Top(n => n.GetViewport() == viewport) is not null;

    /// <summary>Closes <see cref="Topmost"/>; false when nothing is open.</summary>
    public static bool CloseTopmost()
    {
        if (TopLayer(_ => true) is not { } layer)
            return false;
        layer.Close();
        return true;
    }

    private static Node? Top(Func<Node, bool> which) => TopLayer(which)?.Owner;

    private static Layer? TopLayer(Func<Node, bool> which)
    {
        Layer? top = null;
        foreach (var layer in Layers)
        {
            if (GodotObject.IsInstanceValid(layer.Owner) && layer.Owner.IsInsideTree() && which(layer.Owner) && layer.IsOpen() && (top is null || layer.Order > top.Order))
                top = layer;
        }
        return top;
    }
}
