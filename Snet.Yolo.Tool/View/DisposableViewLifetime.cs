using System.Windows;
using System.Windows.Controls;

namespace Snet.Yolo.Tool.View;

internal static class DisposableViewLifetime
{
    public static void Attach(UserControl view)
    {
        Window? owner = null;
        EventHandler? onClosed = null;
        onClosed = (_, _) =>
        {
            if (owner is not null) { owner.Closed -= onClosed; }
            owner = null;
            (view.DataContext as IDisposable)?.Dispose();
        };
        view.Loaded += (_, _) =>
        {
            var window = Window.GetWindow(view);
            if (window is null || ReferenceEquals(window, owner)) { return; }
            if (owner is not null) { owner.Closed -= onClosed; }
            owner = window;
            window.Closed += onClosed;
        };
    }
}
