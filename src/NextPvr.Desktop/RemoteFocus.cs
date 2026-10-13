using Avalonia.Controls;
using Avalonia.VisualTree;

namespace NextPvr.Desktop;

internal static class RemoteFocus
{
    public static void Move(Control window, bool backwards)
    {
        var controls = window.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox or ListBox && c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToArray();
        if (controls.Length == 0) return;
        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
        int index = Array.FindIndex(controls, c => ReferenceEquals(c, focused) || c.IsKeyboardFocusWithin);
        controls[(index + (backwards ? -1 : 1) + controls.Length) % controls.Length].Focus(Avalonia.Input.NavigationMethod.Directional);
    }
}
