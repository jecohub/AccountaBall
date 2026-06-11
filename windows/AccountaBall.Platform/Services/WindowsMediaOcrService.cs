using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace AccountaBall.Platform.Services;

/// <see cref="IOcrService"/> backed by <c>Windows.Media.Ocr</c> — fully offline,
/// no third-party dependency (the Windows analog of Vision.framework). The engine
/// created from the user's profile languages is cached; recognition runs on the
/// thread-pool via WinRT's async, off the UI thread.
public sealed class WindowsMediaOcrService : IOcrService
{
    private readonly OcrEngine? _engine;

    public WindowsMediaOcrService()
    {
        // Null when no OCR language pack is installed for the user's languages.
        // We degrade to "" rather than throw so the capture loop keeps running
        // (a no-text cycle simply reads as off-task-neutral, never a crash).
        _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// True when at least one OCR language pack is available. Surfaced so the App
    /// layer can show a setup hint if false.
    public bool IsAvailable => _engine is not null;

    public async Task<string> RecognizeTextAsync(SoftwareBitmap bitmap)
    {
        if (_engine is null) return string.Empty;

        // OcrEngine requires Bgra8 (premultiplied or straight). Convert defensively
        // so callers can hand us whatever the frame pool produced.
        var input = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? bitmap
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        try
        {
            var result = await _engine.RecognizeAsync(input);
            return result?.Text ?? string.Empty;
        }
        catch (Exception)
        {
            // A malformed/oversized frame must not take the loop down.
            return string.Empty;
        }
        finally
        {
            if (!ReferenceEquals(input, bitmap)) input.Dispose();
        }
    }
}
