using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace AccountaBall.Platform.Services;

/// A single captured frame, owned by the caller. Dispose after OCR. Wraps the
/// <c>SoftwareBitmap</c> so the OCR/capture pair stays decoupled from the D3D
/// plumbing that produced it.
public sealed class CapturedFrame : IDisposable
{
    /// Bgra8 bitmap suitable for <see cref="IOcrService"/>. Within
    /// <c>OcrEngine.MaxImageDimension</c>.
    public SoftwareBitmap Bitmap { get; }

    /// Foreground window title at capture time (best-effort; "" if unavailable).
    public string WindowTitle { get; }

    public CapturedFrame(SoftwareBitmap bitmap, string windowTitle)
    {
        Bitmap = bitmap;
        WindowTitle = windowTitle;
    }

    public void Dispose() => Bitmap.Dispose();
}

/// Periodic screen capture (macOS analog: ScreenCaptureKit). The App-layer loop
/// pulls one frame every <c>AppConstants.CycleSeconds</c>; M3.3 backs this with
/// <c>Windows.Graphics.Capture</c>. Returns null when there is nothing to capture
/// (no foreground window / capture not permitted) — the caller skips the cycle.
public interface IScreenCapture
{
    /// Grab one frame of the focused window. Null = skip this cycle.
    Task<CapturedFrame?> CaptureForegroundAsync(CancellationToken ct = default);
}
