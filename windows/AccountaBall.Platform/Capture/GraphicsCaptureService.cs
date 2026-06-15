using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AccountaBall.Platform.Services;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace AccountaBall.Platform.Capture;

/// <see cref="IScreenCapture"/> over <c>Windows.Graphics.Capture</c> (the macOS
/// ScreenCaptureKit analog). One frame of the focused window is pulled on demand
/// every cycle.
///
/// IMPORTANT (WGC frame delivery): the frame pool only raises <c>FrameArrived</c>
/// when the captured window composes a *new* frame. A window sitting still (a
/// static web page, a paused editor) composes nothing, so "wait for the next
/// FrameArrived" on a long-lived session hangs until the safety timeout — every
/// cycle returns null and the whole accountability loop silently does nothing.
/// The one frame WGC *guarantees* is the initial one right after
/// <c>StartCapture()</c>. So we follow the standard single-shot screenshot recipe:
/// build a fresh pool+session each capture, attach the handler *before*
/// <c>StartCapture()</c>, grab that first frame, then tear the session down. The
/// D3D device is the one expensive object and is kept alive across calls.
///
/// NOTE: WGC + D3D11 interop is the most interop-heavy surface in the port —
/// validate this end-to-end on a real box (compile + a real capture) before
/// trusting it. Behavior (focused window, 3s cadence) is driven by the App loop.
public sealed class GraphicsCaptureService : IScreenCapture, IDisposable
{
    private readonly IDirect3DDevice _device = Direct3D11Interop.CreateDevice();
    private readonly object _gate = new();
    private bool _disposed;

    public async Task<CapturedFrame?> CaptureForegroundAsync(CancellationToken ct = default)
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;

        // Never capture our own windows (the macOS ScreenCaptureKit path excludes
        // the app's own panel too). When a card — e.g. the OffTask prompt — takes
        // foreground, capturing it is both wrong (we'd OCR our own UI) and unsafe:
        // building+disposing a WGC session over our own composing window from the UI
        // thread can deadlock the capture tick. Skip the cycle; the engine keeps its
        // current phase until focus returns to a real window.
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == NativeMethods.CurrentProcessId) return null;

        GraphicsCaptureItem item;
        Direct3D11CaptureFramePool pool;
        GraphicsCaptureSession session;
        lock (_gate)
        {
            if (_disposed) return null;
            try
            {
                item = CaptureItemInterop.CreateForWindow(hwnd);
                var size = item.Size;
                if (size.Width <= 0 || size.Height <= 0) return null;

                pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                session = pool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled = false;
            }
            catch (Exception)
            {
                // Non-capturable foreground window (shell, elevated, secure-desktop,
                // or one that vanished mid-build). Skip this cycle; the next tick
                // retries once focus may have moved to a capturable window.
                return null;
            }
        }

        try
        {
            // NOTE: no ConfigureAwait(false) — the item/session are created on (and
            // STA-bound to) the caller's UI thread, so the continuation that runs the
            // finally below must resume on that same thread. Disposing the session
            // from a pool thread throws 0x8001010E ("marshalled for a different
            // thread") and loses the frame we just captured.
            var bitmap = await GrabFirstFrameAsync(pool, session, ct);
            if (bitmap is null) return null;
            return new CapturedFrame(bitmap, ReadWindowTitle(hwnd));
        }
        finally
        {
            session.Dispose();
            pool.Dispose();
        }
    }

    /// Attach the handler, start the session, and resolve with the first composed
    /// frame copied to a <c>SoftwareBitmap</c> (valid only while the frame is alive,
    /// so we copy inside the handler). The first frame after <c>StartCapture()</c>
    /// is guaranteed even for a static window, so this returns promptly; the timeout
    /// only guards a window that vanishes or refuses to compose at all.
    private Task<SoftwareBitmap?> GrabFirstFrameAsync(
        Direct3D11CaptureFramePool pool, GraphicsCaptureSession session, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<SoftwareBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
        handler = async (sender, _) =>
        {
            // Detach immediately so we service exactly one frame per call.
            sender.FrameArrived -= handler;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) { tcs.TrySetResult(null); return; }
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                tcs.TrySetResult(bitmap);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        pool.FrameArrived += handler;

        var registration = ct.Register(() =>
        {
            pool.FrameArrived -= handler;
            tcs.TrySetCanceled(ct);
        });
        // Safety timeout (~1 cycle): a window that never composes its first frame.
        _ = Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None).ContinueWith(_ =>
        {
            pool.FrameArrived -= handler;
            tcs.TrySetResult(null);
        }, TaskScheduler.Default);

        // Handler is wired up before we start, so the guaranteed initial frame is
        // not missed.
        try { session.StartCapture(); }
        catch (Exception) { pool.FrameArrived -= handler; tcs.TrySetResult(null); }

        return tcs.Task.ContinueWith(t =>
        {
            registration.Dispose();
            return t.IsCompletedSuccessfully ? t.Result : null;
        }, TaskScheduler.Default);
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (_device is IDisposable d) d.Dispose();
    }
}

/// Turns an HWND into a <see cref="GraphicsCaptureItem"/> via the interop factory.
internal static class CaptureItemInterop
{
    // IID of GraphicsCaptureItem (for CreateForWindow's out-iid arg).
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr abi = interop.CreateForWindow(hwnd, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }
}

internal static class NativeMethods
{
    /// Our own process id, cached — capture skips any foreground window we own.
    public static readonly uint CurrentProcessId =
        (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
}
