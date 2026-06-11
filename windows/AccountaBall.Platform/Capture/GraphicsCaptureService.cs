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
/// every cycle. A capture session is kept alive per foreground HWND and rebuilt
/// when focus moves to a different window; <see cref="CaptureForegroundAsync"/>
/// awaits the next composed frame and copies it to a <c>SoftwareBitmap</c> for OCR.
///
/// NOTE: WGC + D3D11 interop is the most interop-heavy surface in the port —
/// validate this end-to-end on a Windows 11 box (compile + a real capture) before
/// trusting it. Behavior (focused window, 3s cadence) is driven by the App loop.
public sealed class GraphicsCaptureService : IScreenCapture, IDisposable
{
    private readonly IDirect3DDevice _device = Direct3D11Interop.CreateDevice();

    // Per-HWND session state, rebuilt on focus change.
    private IntPtr _currentHwnd;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Windows.Graphics.SizeInt32 _lastSize;

    private readonly object _gate = new();

    public async Task<CapturedFrame?> CaptureForegroundAsync(CancellationToken ct = default)
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;

        EnsureSessionForWindow(hwnd);
        if (_framePool is null) return null;

        var bitmap = await GetNextBitmapAsync(ct).ConfigureAwait(false);
        if (bitmap is null) return null;

        return new CapturedFrame(bitmap, ReadWindowTitle(hwnd));
    }

    /// (Re)build the item/pool/session when focus moves to a new window.
    private void EnsureSessionForWindow(IntPtr hwnd)
    {
        lock (_gate)
        {
            if (hwnd == _currentHwnd && _framePool is not null) return;

            TearDownSession();

            var item = CaptureItemInterop.CreateForWindow(hwnd);
            var size = item.Size;
            var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;

            item.Closed += (_, _) => { lock (_gate) { if (_currentHwnd == hwnd) TearDownSession(); } };

            _currentHwnd = hwnd;
            _item = item;
            _framePool = pool;
            _session = session;
            _lastSize = size;

            session.StartCapture();
        }
    }

    /// Await the next composed frame and copy it to a <c>SoftwareBitmap</c> while
    /// the frame is still alive (the surface is only valid until the frame is
    /// disposed and its pool buffer recycled). Short timeout so a stalled window
    /// doesn't hang the cycle; recreates the pool if the window resized.
    private Task<SoftwareBitmap?> GetNextBitmapAsync(CancellationToken ct)
    {
        var pool = _framePool;
        if (pool is null) return Task.FromResult<SoftwareBitmap?>(null);

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

                if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
                {
                    _lastSize = frame.ContentSize;
                    sender.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, frame.ContentSize);
                }

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
        // Safety timeout (~2 cycles): a window that never composes a frame.
        _ = Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None).ContinueWith(_ =>
        {
            pool.FrameArrived -= handler;
            tcs.TrySetResult(null);
        }, TaskScheduler.Default);

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

    private void TearDownSession()
    {
        _session?.Dispose();
        _framePool?.Dispose();
        _session = null;
        _framePool = null;
        _item = null;
        _currentHwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        lock (_gate) TearDownSession();
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
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
}
