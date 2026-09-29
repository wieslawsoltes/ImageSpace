using Windows.System;

namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
        & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void KeyDownHandler(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _dialogOpen || _busy || XamlRoot is null)
            return;
        if (e.Key == VirtualKey.Escape)
        {
            // Custom Popup menus do not own keyboard focus on every Uno host.
            // Dismiss those before routing Escape to a canvas transaction. Do not
            // dismiss dialogs or non-light-dismiss windows through this path.
            var menus = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .Where(popup => popup.IsOpen && popup.IsLightDismissEnabled).ToArray();
            if (menus.Length > 0)
            {
                foreach (var menu in menus)
                    menu.IsOpen = false;
                e.Handled = true;
                return;
            }
        }
        var focus = FocusManager.GetFocusedElement(XamlRoot);
        if (focus is TextBox or PasswordBox || e.OriginalSource is TextBox or PasswordBox)
            return;
        var control = Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
        var shift = Down(VirtualKey.Shift);
        var alt = Down(VirtualKey.Menu);
        if (e.Key == VirtualKey.Space)
        {
            Surface.IsSpaceDown = true;
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape)
        {
            Surface.CancelGesture();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Enter)
        {
            Run(Surface.ApplyCrop);
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.F1)
        {
            _ = HelpAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Tab)
        {
            TogglePanels();
            e.Handled = true;
            return;
        }
        if (control)
        {
            Action? action = e.Key switch
            {
                VirtualKey.N => shift ? () => Run(() => Session.AddLayer("Layer " + (Session.Document.Layers.Count + 1))) : () => _ = NewAsync(),
                VirtualKey.O => () => _ = OpenAsync(false),
                VirtualKey.S => () => _ = SaveAsync(),
                VirtualKey.Z => () => Run(shift ? Session.Redo : Session.Undo),
                VirtualKey.Y => () => Run(Session.Redo),
                VirtualKey.G when alt => () => Run(Session.ToggleClippingMask),
                VirtualKey.J => () => Run(Session.DuplicateLayer),
                VirtualKey.A => SelectAll,
                VirtualKey.D => Deselect,
                VirtualKey.I => shift ? InvertSelection : () => _ = ApplyFilterAsync(FilterKind.Invert),
                VirtualKey.C => () => Copy(false),
                VirtualKey.X => () => Copy(true),
                VirtualKey.V => Paste,
                VirtualKey.E => MergeDown,
                VirtualKey.M => () => AddAdjustment(AdjustmentKind.Curves),
                VirtualKey.L => () => AddAdjustment(AdjustmentKind.Levels),
                VirtualKey.Number0 => Surface.Fit,
                VirtualKey.Number1 => () => Surface.SetZoom(1),
                VirtualKey.R => () => { Surface.ShowRulers = !Surface.ShowRulers; Surface.Invalidate(); }
                ,
                VirtualKey.T => () => { SelectTool(EditorTool.Move); Surface.ShowTransform = true; Surface.InvalidateOverlay(); }
                ,
                VirtualKey.Back => () => Fill(Surface.BackgroundColor),
                _ => null
            };
            if (action is not null)
            {
                action();
                e.Handled = true;
            }
            return;
        }
        if (alt && e.Key == VirtualKey.Back)
        {
            Fill(Surface.Foreground);
            e.Handled = true;
            return;
        }
        if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            Run(Session.ClearPixels);
            e.Handled = true;
            return;
        }
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var amount = shift ? 10 : 1;
            Run(() => Session.NudgeActiveTarget(
                e.Key == VirtualKey.Left ? -amount : e.Key == VirtualKey.Right ? amount : 0,
                e.Key == VirtualKey.Up ? -amount : e.Key == VirtualKey.Down ? amount : 0));
            e.Handled = true;
            return;
        }
        if ((int)e.Key is 219 or 221)
        {
            Surface.Brush = Surface.Brush with
            {
                Size = Math.Clamp(Surface.Brush.Size * ((int)e.Key == 219 ? .8f : 1.25f), 1, 1024)
            };
            RefreshOptions();
            Surface.InvalidateOverlay();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.X)
        {
            (Surface.Foreground, Surface.BackgroundColor) = (Surface.BackgroundColor, Surface.Foreground);
            RefreshColors();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.D)
        {
            Surface.Foreground = Rgba32.Black;
            Surface.BackgroundColor = Rgba32.White;
            RefreshColors();
            e.Handled = true;
            return;
        }
        EditorTool? tool = e.Key switch
        {
            VirtualKey.V => EditorTool.Move,
            VirtualKey.M => shift ? EditorTool.EllipseSelect : EditorTool.Marquee,
            VirtualKey.L => EditorTool.Lasso,
            VirtualKey.W => EditorTool.Wand,
            VirtualKey.C => EditorTool.Crop,
            VirtualKey.I => EditorTool.Eyedropper,
            VirtualKey.B => shift ? EditorTool.Pencil : EditorTool.Brush,
            VirtualKey.E => EditorTool.Eraser,
            VirtualKey.S => EditorTool.Clone,
            VirtualKey.R => EditorTool.Smudge,
            VirtualKey.G => shift ? EditorTool.Fill : EditorTool.Gradient,
            VirtualKey.O => shift ? EditorTool.Burn : EditorTool.Dodge,
            VirtualKey.T => EditorTool.Text,
            VirtualKey.U => shift ? EditorTool.Ellipse : EditorTool.Rectangle,
            VirtualKey.H => EditorTool.Hand,
            VirtualKey.Z => EditorTool.Zoom,
            _ => null
        };
        if (tool is { } selected)
        {
            SelectTool(selected);
            e.Handled = true;
        }
    }
}
