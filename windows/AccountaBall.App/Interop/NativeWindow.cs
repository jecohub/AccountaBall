using System;
using System.Runtime.InteropServices;

namespace AccountaBall.App.Interop;

/// Win32 glue for the floating ball: apply the extended window styles WinUI's
/// AppWindow/Presenter can't (<c>WS_EX_NOACTIVATE</c> so clicking the ball never
/// steals focus from the app being watched, <c>WS_EX_TOOLWINDOW</c> to keep it off
/// the Alt-Tab switcher and taskbar). Port of the macOS NSPanel
/// nonactivatingPanel / .floating-level behavior.
internal static class NativeWindow
{
    private const int GWL_EXSTYLE = -20;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_LAYERED = 0x00080000;

    public static void AddExStyles(IntPtr hwnd, long styles)
    {
        long current = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, current | styles);
    }

    // 64-bit-safe GetWindowLongPtr/SetWindowLongPtr. On 32-bit Windows the *Ptr
    // entry points don't exist, so fall back to the 32-bit variants.
    public static long GetWindowLongPtr(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    public static long SetWindowLongPtr(IntPtr hwnd, int index, long value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, new IntPtr(value)).ToInt64()
            : SetWindowLong32(hwnd, index, (int)value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    /// Monitor DPI for the window (96 = 100%). Used to convert logical card sizes
    /// to the physical pixels AppWindow.Resize expects, per-monitor.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // --- Window region clipping (round ball / rounded cards) ---

    /// Clip the window to an ellipse (the round ball) or a rounded rect (cards),
    /// in physical pixels. SetWindowRgn takes ownership of the region — do not
    /// delete it afterwards. Pass an empty rect to clear back to rectangular.
    public static void SetRoundRegion(IntPtr hwnd, int w, int h, int cornerRadius, bool ellipse)
    {
        IntPtr rgn = ellipse
            ? CreateEllipticRgn(0, 0, w + 1, h + 1)
            : CreateRoundRectRgn(0, 0, w + 1, h + 1, cornerRadius, cornerRadius);
        SetWindowRgn(hwnd, rgn, true);
    }

    public static void ClearRegion(IntPtr hwnd) => SetWindowRgn(hwnd, IntPtr.Zero, true);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool redraw);
}
