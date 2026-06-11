using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace AccountaBall.Platform.Services;

/// Offline OCR over a captured frame. Windows analog of the macOS Vision.framework
/// path. Lives in the Platform layer (not Core) because the engine is fed plain
/// screen text and never references frames. M3.2 backs this with
/// <c>Windows.Media.Ocr</c>; the App layer chains capture -> OCR -> AI -> engine.
public interface IOcrService
{
    /// Recognize all text in a frame, joined top-to-bottom. Returns "" when no
    /// language pack is available or the frame yields nothing. Never throws for an
    /// empty result.
    Task<string> RecognizeTextAsync(SoftwareBitmap bitmap);
}
