using System;
using AccountaBall.App.Interop;
using AccountaBall.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace AccountaBall.App.Shell;

/// Configures the host <see cref="Window"/> as the always-on-top, borderless,
/// no-activate floating ball and resizes it per <see cref="AppPhase"/>. Port of the
/// macOS <c>FloatingPanel</c> (NSPanel, nonactivating, .floating level,
/// resize(for:)). Sizes are logical (DIPs); we scale by the window's monitor DPI
/// and re-anchor to the top-right of the work area so the card grows from a stable
/// corner.
public sealed class FloatingPanel
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;

    /// Margin from the work-area edges, in logical units.
    private const double EdgeMargin = 24;

    public FloatingPanel(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        ConfigurePresenter();
        _appWindow.IsShownInSwitchers = false;   // off Alt-Tab (TOOLWINDOW also enforces this)
        NativeWindow.AddExStyles(_hwnd, NativeWindow.WS_EX_NOACTIVATE | NativeWindow.WS_EX_TOOLWINDOW);
    }

    private void ConfigurePresenter()
    {
        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
    }

    /// Resize + re-anchor to top-right for the given phase.
    public void ResizeFor(AppPhase phase)
    {
        var (w, h) = SizeFor(phase);
        double scale = NativeWindow.GetDpiForWindow(_hwnd) / 96.0;
        int pw = (int)Math.Round(w * scale);
        int ph = (int)Math.Round(h * scale);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(pw, ph));

        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            int margin = (int)Math.Round(EdgeMargin * scale);
            int x = area.WorkArea.X + area.WorkArea.Width - pw - margin;
            int y = area.WorkArea.Y + margin;
            _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }

    /// Per-phase logical sizes. The compact ball (Idle/Session) is small and round;
    /// the cards grow for content. NOTE: approximated from the macOS layout — tune
    /// against the real cards in the M4.5 feel-it pass.
    private static (double W, double H) SizeFor(AppPhase phase) => phase switch
    {
        AppPhase.Idle => (110, 110),
        AppPhase.Session => (110, 110),
        AppPhase.Welcome => (360, 440),
        AppPhase.Setup => (440, 560),
        AppPhase.WhatsUp => (360, 300),
        AppPhase.Ambiguous => (380, 320),
        AppPhase.OffTask => (380, 360),
        AppPhase.Progress => (360, 300),
        AppPhase.Complete => (440, 580),
        AppPhase.AiUnavailable => (400, 360),
        AppPhase.FreeBall => (110, 110),
        AppPhase.FreeBallLog => (440, 560),
        AppPhase.FreeBallRecap => (440, 580),
        AppPhase.FreeBallHistory => (460, 600),
        _ => (110, 110),
    };
}
